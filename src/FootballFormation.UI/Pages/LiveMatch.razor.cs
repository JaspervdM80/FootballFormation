using System.Timers;
using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>One entry on the match timeline — a goal or a substitution — so both can be listed together.</summary>
public record MatchEvent(int Minute, bool IsGoal, GameGoal? Goal, GameSubstitution? Substitution);

/// <summary>
/// The sideline screen. An admin runs the clock and records what happens; everyone else sees the
/// same page read-only and updating live, so there is one URL to share during a match.
/// </summary>
public partial class LiveMatch : IDisposable
{
    [Inject] private LiveMatchService Live { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private LiveMatchNotifier Notifier { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private List<Player> AllPlayers { get; set; } = [];
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;
    private bool _isAdmin;

    /// <summary>
    /// Drives the clock display only. The elapsed value is derived from the anchor the server
    /// stored, so a tick never talks to the server and every viewer shows the same time.
    /// </summary>
    private System.Timers.Timer? _tick;

    private int ElapsedSeconds => GameData?.ElapsedSecondsAt(DateTime.UtcNow) ?? 0;

    private string ClockDisplay => $"{ElapsedSeconds / 60:D2}:{ElapsedSeconds % 60:D2}";

    /// <summary>
    /// The period on screen: the one being played; at the break and after the whistle the last one
    /// that was; and before kick-off the first one, so the pitch is never blank when a line-up exists.
    /// </summary>
    private GamePeriod? DisplayPeriod
    {
        get
        {
            if (GameData is null) return null;
            if (GameData.LivePeriodId is { } liveId)
                return GameData.Periods.FirstOrDefault(p => p.Id == liveId);

            var lastPlayed = GameData.Periods
                .Where(p => p.StartedAtSeconds is not null)
                .OrderByDescending(p => p.StartedAtSeconds)
                .FirstOrDefault();

            return lastPlayed ?? GameData.Periods.OrderBy(p => p.PeriodType).FirstOrDefault();
        }
    }

    private bool IsLivePeriod => GameData?.LivePeriodId is not null;

    private List<GamePlayerPosition> DisplayLineup => DisplayPeriod?.PlayerPositions ?? [];

    private List<GamePlayerPosition> OnPitch => [.. DisplayLineup.Where(p => !p.IsSubstitute)];

    private List<GamePlayerPosition> OnBench => [.. DisplayLineup.Where(p => p.IsSubstitute)];

    private FormationType DisplayFormation =>
        DisplayPeriod?.FormationTypeOverride ?? GameData?.FormationType ?? FormationType.F442;

    /// <summary>Whether the sub controls can do anything — needs an admin and a period in play.</summary>
    private bool CanSubstitute => _isAdmin && IsLivePeriod;

    /// <summary>True once every period has been kicked off, so "next period" has nowhere to go.</summary>
    private bool AllPeriodsPlayed =>
        GameData is not null && GameData.Periods.All(p => p.StartedAtSeconds is not null);

    /// <summary>Singular noun for this game's periods — "half" or "quarter".</summary>
    private string PeriodNoun => GameData?.SplitType.PeriodLabel() ?? "half";

    /// <summary>What the match is doing right now, in one phrase under the clock.</summary>
    private string StatusLabel => GameData?.MatchState switch
    {
        null or MatchState.NotStarted => L["Not started"],
        MatchState.Finished => L["Full time"],
        _ when !IsLivePeriod => L["Break"],
        _ when !GameData.IsClockRunning => L["Paused"],
        _ => L[DisplayPeriod?.PeriodType.DisplayName() ?? "In progress"]
    };

    /// <summary>Drives the colour of the status chip: only a running clock counts as live.</summary>
    private string StatusCssClass => GameData?.MatchState switch
    {
        MatchState.InProgress when GameData.IsClockRunning => "live-status live-status-running",
        MatchState.InProgress => "live-status live-status-paused",
        MatchState.Finished => "live-status live-status-done",
        _ => "live-status"
    };

    /// <summary>
    /// Who can be credited with a goal: the players on the pitch first, then the bench, then
    /// anyone else in the roster. Scoring is nearly always someone currently playing, and this
    /// puts them where a thumb lands first.
    /// </summary>
    private List<Player> GoalCandidates
    {
        get
        {
            if (GameData is null) return [];

            var onPitch = OnPitch.Select(p => p.PlayerId).ToList();
            var onBench = OnBench.Select(p => p.PlayerId).ToList();
            var roster = GameData.SelectRoster(AllPlayers, Squad).Select(p => p.Id);

            var ordered = onPitch.Concat(onBench).Concat(roster).Distinct();
            return [.. ordered.Select(FindPlayer).OfType<Player>()];
        }
    }

    /// <summary>
    /// Who can come on: the bench for this period, plus anyone in the roster with no lineup entry
    /// at all — a late arrival should not be locked out of a match already under way.
    /// </summary>
    private List<Player> SubCandidates
    {
        get
        {
            if (GameData is null) return [];

            var inLineup = DisplayLineup.Select(p => p.PlayerId).ToHashSet();
            var bench = OnBench.Select(p => FindPlayer(p.PlayerId)).OfType<Player>();
            var unlisted = GameData.SelectRoster(AllPlayers, Squad).Where(p => !inLineup.Contains(p.Id));

            return [.. bench.Concat(unlisted)];
        }
    }

    /// <summary>Goals and substitutions on one timeline, most recent first.</summary>
    private List<MatchEvent> Timeline
    {
        get
        {
            if (GameData is null) return [];

            var goals = GameData.Goals.Select(g => new MatchEvent(g.Minute ?? 0, true, g, null));
            var subs = GameData.Substitutions.Select(s => new MatchEvent(s.Minute, false, null, s));

            return [.. goals.Concat(subs).OrderByDescending(e => e.Minute)];
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        _isAdmin = authState.User.Identity?.IsAuthenticated == true;

        if (!await ReloadAsync()) return;

        // Anyone who appeared stays nameable regardless of current squad membership, matching
        // how MatchResult loads its player pool.
        var playersResult = await PlayerService.GetAllAsync();
        AllPlayers = playersResult.IsSuccess ? playersResult.Value! : [];

        var squadResult = await SquadService.GetSquadAsync(GameData!.SeasonId);
        Squad = squadResult.IsSuccess ? squadResult.Value! : SeasonSquad.Empty;

        Notifier.Changed += OnLiveChanged;

        _tick = new System.Timers.Timer(1000);
        _tick.Elapsed += OnTick;
        _tick.Start();
    }

    /// <summary>Only repaints while the clock is actually moving — a paused screen has nothing to redraw.</summary>
    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (GameData?.IsClockRunning != true) return;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Someone changed this match — possibly in another browser. Reloading rather than patching
    /// keeps every viewer showing exactly what is stored.
    /// </summary>
    private void OnLiveChanged(int gameId)
    {
        if (gameId != GameId) return;

        _ = InvokeAsync(async () =>
        {
            await ReloadAsync();
            StateHasChanged();
        });
    }

    private async Task<bool> ReloadAsync()
    {
        var result = await Live.GetLiveAsync(GameId);
        if (!Snackbar.ReportFailure(result))
        {
            Navigation.NavigateTo("/games");
            return false;
        }

        GameData = result.Value!;
        return true;
    }

    // Clock controls. Each service call notifies every viewer, which is what reloads this page too.
    private async Task StartMatch() =>
        Snackbar.Report(await Live.StartMatchAsync(GameId), L["Match started"]);

    private async Task PauseClock() =>
        Snackbar.Report(await Live.PauseClockAsync(GameId), L["Clock paused"], Severity.Info);

    private async Task ResumeClock() =>
        Snackbar.Report(await Live.ResumeClockAsync(GameId), L["Clock running"], Severity.Info);

    private async Task EndPeriod() =>
        Snackbar.Report(await Live.EndPeriodAsync(GameId), L["Period ended"], Severity.Info);

    private async Task StartNextPeriod() =>
        Snackbar.Report(await Live.StartNextPeriodAsync(GameId), L["Next period started"]);

    private async Task FinishMatch()
    {
        var confirmed = await DialogService.ConfirmAsync(
            L["Finish match"],
            L["End the match and save the final score? You can still edit the result afterwards."],
            "Finish match");
        if (!confirmed) return;

        Snackbar.Report(await Live.FinishMatchAsync(GameId), L["Match finished"]);
    }

    private async Task AddGoal()
    {
        var parameters = new DialogParameters<LiveGoalDialog>
        {
            { x => x.Candidates, GoalCandidates }
        };

        var dialog = await DialogService.ShowAsync<LiveGoalDialog>(
            L["Goal"], parameters, UiFeedback.LockedDialog);
        var result = await dialog.Result;

        if (result is not { Canceled: false, Data: LiveGoalChoice choice }) return;

        var logged = await Live.LogGoalAsync(
            GameId, choice.ScorerId, choice.AssisterId, choice.IsOwnGoal, isOpponentGoal: false);
        Snackbar.Report(logged, L["Goal added!"]);
    }

    private async Task AddOpponentGoal()
    {
        var logged = await Live.LogGoalAsync(
            GameId, scorerId: null, assisterId: null, isOwnGoal: false, isOpponentGoal: true);
        Snackbar.Report(logged, L["Opponent goal added"], Severity.Info);
    }

    private async Task RemoveGoal(GameGoal goal) =>
        Snackbar.Report(await Live.RemoveGoalAsync(GameId, goal.Id), L["Goal removed"], Severity.Warning);

    private async Task RemoveSubstitution(GameSubstitution sub) =>
        Snackbar.Report(await Live.RemoveSubstitutionAsync(sub.Id),
            L["Substitution undone"], Severity.Warning);

    /// <summary>Tapping a player on the pitch asks who replaces them.</summary>
    private async Task OpenSubDialog(int playerOffId)
    {
        if (!CanSubstitute) return;

        var playerOff = FindPlayer(playerOffId);
        if (playerOff is null) return;

        var parameters = new DialogParameters<LiveSubDialog>
        {
            { x => x.PlayerOff, playerOff },
            { x => x.Bench, SubCandidates }
        };

        var dialog = await DialogService.ShowAsync<LiveSubDialog>(
            L["Substitution"], parameters, UiFeedback.LockedDialog);
        var result = await dialog.Result;

        if (result is not { Canceled: false, Data: int playerOnId }) return;

        var sub = await Live.SubstituteAsync(GameId, playerOffId, playerOnId);
        Snackbar.Report(sub, L["Substitution made"]);
    }

    private Player? FindPlayer(int playerId) => AllPlayers.FirstOrDefault(p => p.Id == playerId);

    private string PlayerLabel(int playerId) =>
        FindPlayer(playerId)?.ShortName ?? L["Player {0}", playerId].Value;

    private void NavigateToGames() => Navigation.NavigateTo("/games");

    private void NavigateToResult() => Navigation.NavigateTo($"/games/{GameId}/result");

    public void Dispose()
    {
        Notifier.Changed -= OnLiveChanged;

        if (_tick is null) return;
        _tick.Elapsed -= OnTick;
        _tick.Dispose();
    }
}

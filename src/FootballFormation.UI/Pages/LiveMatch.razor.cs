using System.Timers;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>
/// One entry on the match timeline — a goal or a substitution — so both can be listed together.
/// <paramref name="RecordedAt"/> orders events that share a minute, which the minute alone cannot.
/// </summary>
public record MatchEvent(int Minute, DateTime RecordedAt, bool IsGoal, GameGoal? Goal, GameSubstitution? Substitution);

/// <summary>
/// The sideline screen. An admin runs the clock and records what happens; everyone else sees the
/// same page read-only and updating live, so there is one URL to share during a match.
/// </summary>
public partial class LiveMatch
{
    [Inject] private LiveMatchService Live { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private LiveMatchNotifier Notifier { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
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

    /// <summary>The real time the match has been running. What gets stored and counted.</summary>
    private int ElapsedSeconds => GameData?.ElapsedSecondsAt(DateTime.UtcNow) ?? 0;

    /// <summary>
    /// The same instant as a scoreboard shows it: capped at the end of the half, with the overrun
    /// reported as additional time, and the second half starting at half the match duration.
    /// </summary>
    private MatchClock Clock => GameData is null
        ? MatchClock.BeforeKickOff
        : MatchClockReport.Build(GameData, DisplayPeriod, ElapsedSeconds);

    private static string Mmss(int seconds) => $"{seconds / 60:D2}:{seconds % 60:D2}";

    private string ClockDisplay => Mmss(Clock.Seconds);

    private string AdditionalDisplay => Mmss(Clock.AdditionalSeconds);

    /// <summary>
    /// The period on screen: the one being played; at the break and after the whistle the last one
    /// that was; and before kick-off the first one, so the pitch is never blank when a line-up exists.
    /// </summary>
    private GamePeriod? DisplayPeriod => GameData?.CurrentOrLastPeriod();

    private bool IsLivePeriod => GameData?.LivePeriodId is not null;

    private List<GamePlayerPosition> DisplayLineup => DisplayPeriod?.PlayerPositions ?? [];

    private List<GamePlayerPosition> OnPitch => [.. DisplayLineup.Where(p => !p.IsSubstitute)];

    /// <summary>
    /// The bench for this period. A lineup can outlive the roster it was built from — someone
    /// marked unavailable, or dropped from the squad, keeps their saved substitute row — and
    /// listing them as a sub would offer a player who is not at the match.
    /// </summary>
    private List<GamePlayerPosition> OnBench =>
        [.. DisplayLineup.Where(p => p.IsSubstitute && IsInRoster(p.PlayerId))];

    private bool IsInRoster(int playerId) =>
        GameData is not null && FindPlayer(playerId) is { } player && GameData.IsInRoster(player, Squad);

    private FormationType DisplayFormation =>
        DisplayPeriod?.FormationTypeOverride ?? GameData?.FormationType ?? FormationType.F442;

    /// <summary>Whether the sub controls can do anything — needs an admin and a period in play.</summary>
    private bool CanSubstitute => _isAdmin && IsLivePeriod;

    /// <summary>The first period not yet kicked off — where the clock goes next, if anywhere.</summary>
    private GamePeriod? NextPeriod => GameData?.Periods
        .OrderBy(p => p.PeriodType)
        .FirstOrDefault(p => p.StartedAtSeconds is null);

    /// <summary>
    /// The half on the clock, which is the only division this screen names. Quarters exist to
    /// plan two line-ups per half; nobody standing at the pitch thinks in them, so Q1 and Q2 both
    /// read as the first half here and the line-up change between them is announced on its own.
    /// </summary>
    private string? DisplayHalfLabel => DisplayPeriod?.PeriodType.HalfDisplayName();

    /// <summary>Half the buttons would kick off, or null once every period has been played.</summary>
    private string? NextHalfLabel => NextPeriod?.PeriodType.HalfDisplayName();

    /// <summary>
    /// Whether the period being played ends in a real stoppage. Only half time does; a quarter
    /// boundary rolls straight on, so the screen offers the line-up change rather than a whistle.
    /// </summary>
    private bool BreakFollowsCurrentPeriod =>
        DisplayPeriod is { } period && IsLivePeriod && period.PeriodType.IsFollowedByBreak();

    /// <summary>
    /// The period whose line-up takes over partway through the half on screen, if there is one.
    /// Read from the period order rather than the clock, so the changes due can be looked up
    /// before kick-off as well as during play. Null once the match is over.
    /// </summary>
    private GamePeriod? MidHalfSuccessor
    {
        get
        {
            if (GameData is null || GameData.MatchState == MatchState.Finished) return null;
            if (DisplayPeriod is not { } current) return null;

            var next = GameData.Periods
                .OrderBy(p => p.PeriodType)
                .FirstOrDefault(p => p.PeriodType > current.PeriodType);

            return next?.PeriodType.Half() == current.PeriodType.Half() ? next : null;
        }
    }

    /// <summary>
    /// The swaps the planned line-ups imply for the middle of this half, measured against who is
    /// on the pitch right now — so a live substitution already made drops out of the list.
    /// </summary>
    private PlannedChanges PlannedChanges =>
        DisplayPeriod is { } current && MidHalfSuccessor is { } next
            ? PlannedChangesReport.Build(current, next, FindPlayer)
            : PlannedChanges.None;

    /// <summary>What the match is doing right now, in one phrase under the clock.</summary>
    private string StatusLabel => GameData?.MatchState switch
    {
        null or MatchState.NotStarted => L["Not started"],
        MatchState.Finished => L["Full time"],
        _ when !IsLivePeriod => L["Break"],
        _ when !GameData.IsClockRunning => L["Paused"],
        // The half is played out and play has not stopped — the thing to say is how much longer.
        _ when Clock.IsInAdditionalTime => L["Additional time"],
        _ => L[DisplayHalfLabel ?? "In progress"]
    };

    /// <summary>Drives the colour of the status chip: only a running clock counts as live.</summary>
    private string StatusCssClass => GameData?.MatchState switch
    {
        MatchState.InProgress when GameData.IsClockRunning && Clock.IsInAdditionalTime =>
            "live-status live-status-extra",
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

    /// <summary>
    /// Exact minutes on the pitch so far, for the admin deciding who to bring on next. Recomputed
    /// on every render, which is what keeps the running player's total climbing with the clock.
    /// </summary>
    private List<LiveMinutesRow> MinutesPlayed =>
        GameData is null ? [] : LiveMinutesReport.Build(GameData, ElapsedSeconds, FindPlayer);

    /// <summary>Goals and substitutions on one timeline, most recent first.</summary>
    private List<MatchEvent> Timeline
    {
        get
        {
            if (GameData is null) return [];

            var goals = GameData.Goals.Select(g => new MatchEvent(g.Minute ?? 0, g.RecordedAt, true, g, null));
            var subs = GameData.Substitutions.Select(s => new MatchEvent(s.Minute, s.RecordedAt, false, null, s));

            // A goal and the sub that followed it commonly share a minute; the entry time keeps
            // them in the order they actually happened rather than the order they were queried.
            return [.. goals.Concat(subs)
                .OrderByDescending(e => e.Minute)
                .ThenByDescending(e => e.RecordedAt)];
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        _isAdmin = authState.User.IsAdmin();

        if (!await ReloadAsync()) return;

        // Anyone who appeared stays nameable regardless of current squad membership, matching
        // how MatchResult loads its player pool.
        var playersResult = await PlayerService.GetAllAsync(Cancellation);
        AllPlayers = playersResult.IsSuccess ? playersResult.Value! : [];

        var squadResult = await SquadService.GetSquadAsync(GameData!.SeasonId, Cancellation);
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
        var result = await Live.GetLiveAsync(GameId, Cancellation);
        if (!Snackbar.ReportFailure(L, result))
        {
            Trail.Redirect(AppRoutes.Games);
            return false;
        }

        GameData = result.Value!;
        return true;
    }

    // Clock controls. Each service call notifies every viewer, which is what reloads this page too.
    private async Task StartMatch() =>
        Snackbar.Report(L, await Live.StartMatchAsync(GameId), L["Match started"]);

    private async Task PauseClock() =>
        Snackbar.Report(L, await Live.PauseClockAsync(GameId), L["Clock paused"], Severity.Info);

    private async Task ResumeClock() =>
        Snackbar.Report(L, await Live.ResumeClockAsync(GameId), L["Clock running"], Severity.Info);

    private async Task EndPeriod() =>
        Snackbar.Report(L, await Live.EndPeriodAsync(GameId), L["Period ended"], Severity.Info);

    private async Task StartNextPeriod() =>
        Snackbar.Report(L, await Live.StartNextPeriodAsync(GameId), L["Next period started"]);

    private async Task AdvancePeriod() =>
        Snackbar.Report(L, await Live.AdvancePeriodAsync(GameId), L["Next period started"]);

    private async Task FinishMatch()
    {
        var confirmed = await DialogService.ConfirmAsync(
            L["Finish match"],
            L["End the match and save the final score? You can still edit the result afterwards."],
            "Finish match");
        if (!confirmed) return;

        Snackbar.Report(L, await Live.FinishMatchAsync(GameId), L["Match finished"]);
    }

    private async Task AddGoal()
    {
        var choice = await DialogService.PromptAsync<LiveGoalDialog, LiveGoalChoice>(
            L["Goal"], p => p.Add(x => x.Candidates, GoalCandidates));
        if (choice is null) return;

        var logged = await Live.LogGoalAsync(
            GameId, choice.ScorerId, choice.AssisterId, choice.IsOwnGoal, isOpponentGoal: false);
        Snackbar.Report(L, logged, L["Goal added!"]);
    }

    private async Task AddOpponentGoal()
    {
        var logged = await Live.LogGoalAsync(
            GameId, scorerId: null, assisterId: null, isOwnGoal: false, isOpponentGoal: true);
        Snackbar.Report(L, logged, L["Opponent goal added"], Severity.Info);
    }

    private async Task RemoveGoal(GameGoal goal) =>
        Snackbar.Report(L, await Live.RemoveGoalAsync(GameId, goal.Id), L["Goal removed"], Severity.Warning);

    private async Task RemoveSubstitution(GameSubstitution sub) =>
        Snackbar.Report(L, await Live.RemoveSubstitutionAsync(sub.Id),
            L["Substitution undone"], Severity.Warning);

    /// <summary>Tapping a player on the pitch asks who replaces them.</summary>
    private async Task OpenSubDialog(int playerOffId)
    {
        if (!CanSubstitute) return;

        var playerOff = FindPlayer(playerOffId);
        if (playerOff is null) return;

        var playerOnId = await DialogService.PromptValueAsync<LiveSubDialog, int>(
            L["Substitution"],
            p =>
            {
                p.Add(x => x.PlayerOff, playerOff);
                p.Add(x => x.Bench, SubCandidates);
            });
        if (playerOnId is null) return;

        var sub = await Live.SubstituteAsync(GameId, playerOffId, playerOnId.Value);
        Snackbar.Report(L, sub, L["Substitution made"]);
    }

    private Player? FindPlayer(int playerId) => AllPlayers.FirstOrDefault(p => p.Id == playerId);

    private string PlayerLabel(int playerId) =>
        FindPlayer(playerId)?.ShortName ?? L["Player {0}", playerId].Value;

    private void NavigateToResult() => Navigation.NavigateTo(AppRoutes.Result(GameId));

    public override void Dispose()
    {
        Notifier.Changed -= OnLiveChanged;

        if (_tick is not null)
        {
            _tick.Elapsed -= OnTick;
            _tick.Dispose();
        }

        base.Dispose();
    }
}

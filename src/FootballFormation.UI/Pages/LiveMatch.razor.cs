using System.Timers;
using FootballFormation.Core.Reporting;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Pages;

/// One URL for everyone: an admin runs the clock, everyone else sees the same page read-only and updating live.
public partial class LiveMatch
{
    [Inject] private LiveMatchService Live { get; set; } = null!;
    [Inject] private MatchClockService ClockService { get; set; } = null!;
    [Inject] private MatchGoalService GoalService { get; set; } = null!;
    [Inject] private MatchSubstitutionService SubService { get; set; } = null!;
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

    /// Per circuit and deliberately not stored — a glance-vs-detail choice made in the moment. Survives the live reloads because those
    /// replace the data, not the component.
    private bool ShowSubstitutions { get; set; } = true;

    /// Repaint only — the elapsed value comes from the anchor the server stored, so a tick never talks to the server.
    private System.Timers.Timer? _tick;

    /// Real running time, which is what gets stored and counted. <see cref="Clock"/> is the same instant as a scoreboard shows it.
    private int ElapsedSeconds => GameData?.ElapsedSecondsAt(DateTime.UtcNow) ?? 0;

    private MatchClock Clock => GameData is null
        ? MatchClock.BeforeKickOff
        : MatchClockReport.Build(GameData, DisplayHalf, ElapsedSeconds);

    private static string Mmss(int seconds) => $"{seconds / 60:D2}:{seconds % 60:D2}";

    private string ClockDisplay => Mmss(Clock.Seconds);

    private string AdditionalDisplay => Mmss(Clock.AdditionalSeconds);

    /// At the break the pitch shows the half about to be played, so it is the one set up here; everywhere else it is whatever is on the
    /// pitch now or was on it last.
    private GamePeriod? DisplayHalf => IsAtBreak ? NextHalf : GameData?.CurrentOrLastHalf();

    private bool IsHalfInPlay => GameData?.LivePeriodId is not null;

    /// A half has been played, none is live, and one is still to come: the moment the next half's line-up is set, where a change to it is a
    /// half-time substitution the restart will record.
    private bool IsAtBreak =>
        GameData is { MatchState: MatchState.InProgress } && !IsHalfInPlay && NextHalf is not null;

    private List<GamePlayerPosition> DisplayLineup => DisplayHalf?.PlayerPositions ?? [];

    private List<GamePlayerPosition> OnPitch => [.. DisplayLineup.Where(p => !p.IsSubstitute)];

    /// A line-up outlives the roster it was built from, so the roster filter is what stops the bench offering someone who is not at the match.
    private List<GamePlayerPosition> OnBench =>
        [.. DisplayLineup.Where(p => p.IsSubstitute && IsInRoster(p.PlayerId))];

    private bool IsInRoster(int playerId) =>
        GameData is not null && FindPlayer(playerId) is { } player && GameData.IsInRoster(player, Squad);

    private FormationType DisplayFormation =>
        DisplayHalf?.FormationTypeOverride ?? GameData?.FormationType ?? FormationType.F442;

    private bool CanSubstitute => _isAdmin && (IsHalfInPlay || IsAtBreak);

    private GamePeriod? NextHalf => GameData?.NextHalf();

    /// Halves are the only division this screen names — nobody at the pitch thinks in quarters, so a second line-up shows up only under
    /// <see cref="ShowPlannedChanges"/>, as a plan rather than a stage of the match.
    private string? DisplayHalfLabel => DisplayHalf?.PeriodType.HalfDisplayName();

    private string? NextHalfLabel => NextHalf?.PeriodType.HalfDisplayName();

    private bool HalfTimeFollows => IsHalfInPlay && NextHalf is not null;

    /// Measured against who is on the pitch right now, so a change already made by hand drops out. Read off the planned line-ups rather
    /// than the clock, which is what lets the changes due be read before kick-off as well as during play.
    private PlannedChanges PlannedChanges =>
        GameData is { MatchState: not MatchState.Finished } game
        && DisplayHalf is { } half
        && game.MidHalfPlan(half) is { } plan
            ? PlannedChangesReport.Build(half, plan, FindPlayer,
                game.Substitutions.Where(s => s.GamePeriodId == half.Id))
            : PlannedChanges.None;

    private int PlannedChangeCount =>
        PlannedChanges.Substitutions.Count + PlannedChanges.Moves.Count;

    /// Exactly what the restart will record, off the same diff <c>StartNextHalfAsync</c> uses — so the preview never lists a position move
    /// or an injured player's swap that will not reach the timeline.
    private PlannedChanges HalfTimeChanges
    {
        get
        {
            if (!IsAtBreak || GameData is not { } game
                || game.CurrentOrLastHalf() is not { } previous || NextHalf is not { } upcoming)
                return PlannedChanges.None;

            var injured = game.Injuries.Select(i => i.PlayerId).ToHashSet();
            var subs = LineupDiff.Swaps(previous, upcoming, injured)
                .Select(s => new PlannedSubstitution(FindPlayer(s.PlayerOffId), FindPlayer(s.PlayerOnId), s.Position))
                .ToList();

            return new PlannedChanges(subs, []);
        }
    }

    private string StatusLabel => GameData?.MatchState switch
    {
        null or MatchState.NotStarted => L["Not started"],
        MatchState.Finished => L["Full time"],
        _ when !IsHalfInPlay => L["Half time"],
        // The half is played out and play has not stopped — the thing to say is how much longer.
        _ when Clock.IsInAdditionalTime => L["Additional time"],
        _ => L[DisplayHalfLabel ?? "In progress"]
    };

    /// A half being played always has a running clock — nothing stops one short of the whistle — so the third arm here is half time.
    private string StatusCssClass => GameData?.MatchState switch
    {
        MatchState.InProgress when GameData.IsClockRunning && Clock.IsInAdditionalTime =>
            "live-status live-status-extra",
        MatchState.InProgress when GameData.IsClockRunning => "live-status live-status-running",
        MatchState.InProgress => "live-status live-status-break",
        MatchState.Finished => "live-status live-status-done",
        _ => "live-status"
    };

    /// Ordered pitch, bench, rest of roster: a scorer is nearly always someone currently playing, and this puts them where a thumb lands first.
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

    private List<Player> SubCandidates => CandidatesFor(DisplayHalf);

    /// Who could come on in a given half. Taken per half rather than from whatever the screen is showing, because correcting an earlier
    /// substitution has to be judged against the half it belongs to. Includes roster players with no line-up entry there, so a late arrival
    /// is not locked out of a match already under way; the two exclusions on top of <see cref="Game.SelectRoster"/> are a standing injury
    /// and one picked up in this match.
    private List<Player> CandidatesFor(GamePeriod? half)
    {
        if (GameData is null) return [];

        var lineup = half?.PlayerPositions ?? [];
        var hurt = GameData.Injuries.Select(i => i.PlayerId).ToHashSet();
        var inLineup = lineup.Select(p => p.PlayerId).ToHashSet();
        var bench = lineup.Where(p => p.IsSubstitute && IsInRoster(p.PlayerId))
            .Select(p => FindPlayer(p.PlayerId)).OfType<Player>();
        var unlisted = GameData.SelectRoster(AllPlayers, Squad)
            .Where(p => !inLineup.Contains(p.Id) && !Squad.IsInjured(p.Id));

        return [.. bench.Concat(unlisted).Where(p => !hurt.Contains(p.Id))];
    }

    /// Recomputed on every render, which is what keeps a playing total climbing with the clock.
    private List<LiveMinutesRow> MinutesPlayed =>
        GameData is null ? [] : LiveMinutesReport.Build(GameData, ElapsedSeconds, FindPlayer);

    /// False before kick-off, when <see cref="MinutesPlayed"/> is the planned line-up costed at a full period each — a different thing,
    /// and the card has to say so rather than claim minutes nobody has played.
    private bool MinutesAreActual => GameData?.HasActualTimings == true;

    /// Counts only the two kinds <see cref="ShowSubstitutions"/> can hide, so an empty timeline can be told apart from a filtered one.
    private bool HasEvents => GameData is { } game && (game.Goals.Count > 0 || game.Substitutions.Count > 0);

    /// Newest first, the way the live screen reads. Substitutions can be filtered out because heavy rotation buries the goals among them.
    private List<MatchEvent> Timeline =>
        GameData is null ? [] : MatchTimelineReport.Build(GameData, ShowSubstitutions, newestFirst: true);

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        _isAdmin = authState.User.IsAdmin();

        if (!await ReloadAsync()) return;

        // Every player, not the squad: anyone who appeared stays nameable regardless of current membership.
        var playersResult = await PlayerService.GetAllAsync(Cancellation);
        AllPlayers = playersResult.IsSuccess ? playersResult.Value! : [];

        var squadResult = await SquadService.GetSquadAsync(GameData!.SeasonId, Cancellation);
        Squad = squadResult.IsSuccess ? squadResult.Value! : SeasonSquad.Empty;

        Notifier.Changed += OnLiveChanged;

        _tick = new System.Timers.Timer(1000);
        _tick.Elapsed += OnTick;
        _tick.Start();
    }

    /// Only repaints while the clock is moving — at a break there is nothing to redraw.
    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (GameData?.IsClockRunning != true) return;
        _ = InvokeAsync(StateHasChanged);
    }

    /// Reloads rather than patches, so every viewer shows exactly what is stored no matter which browser made the change.
    private void OnLiveChanged(int gameId, LiveMatchEvent change)
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

        // ReportFailure answers false for a cancelled read as well as a failed one, and a visitor who
        // has already left is one Trail.Redirect would leave no way back from.
        if (result.IsCancelled) return false;

        if (!Snackbar.ReportFailure(L, result))
        {
            Trail.Redirect(AppRoutes.Games);
            return false;
        }

        GameData = result.Value!;
        return true;
    }

    // Each clock call notifies every viewer, which is what reloads this page too.
    private async Task StartMatch() =>
        Snackbar.Report(L, await ClockService.StartMatchAsync(GameId), L["Match started"]);

    private async Task EndHalf() =>
        Snackbar.Report(L, await ClockService.EndHalfAsync(GameId), L["Half ended"], Severity.Info);

    private async Task StartNextHalf() =>
        Snackbar.Report(L, await ClockService.StartNextHalfAsync(GameId), L["Next half started"]);

    private Task ShowPlannedChanges() =>
        DialogService.ShowAsync<PlannedChangesDialog>(
            L["Changes to make"],
            new DialogParameters<PlannedChangesDialog> { { x => x.Changes, PlannedChanges } },
            UiFeedback.LockedDialog);

    private async Task FinishMatch()
    {
        var confirmed = await DialogService.ConfirmAsync(
            L["Finish match"],
            L["End the match and save the final score? You can still edit the result afterwards."],
            "Finish match");
        if (!confirmed) return;

        Snackbar.Report(L, await ClockService.FinishMatchAsync(GameId), L["Match finished"]);
    }

    private async Task AddGoal()
    {
        var choice = await DialogService.PromptAsync<LiveGoalDialog, LiveGoalChoice>(
            L["Goal"], p => p.Add(x => x.Candidates, GoalCandidates));
        if (choice is null) return;

        var logged = await GoalService.LogGoalAsync(
            GameId, choice.ScorerId, choice.AssisterId, choice.IsOwnGoal, isOpponentGoal: false);
        Snackbar.Report(L, logged, L["Goal added!"]);
    }

    private async Task AddOpponentGoal()
    {
        var logged = await GoalService.LogGoalAsync(
            GameId, scorerId: null, assisterId: null, isOwnGoal: false, isOpponentGoal: true);
        Snackbar.Report(L, logged, L["Opponent goal added"], Severity.Info);
    }

    private async Task RemoveGoal(GameGoal goal) =>
        Snackbar.Report(L, await GoalService.RemoveGoalAsync(GameId, goal.Id), L["Goal removed"], Severity.Warning);

    private async Task RemoveSubstitution(GameSubstitution sub) =>
        Snackbar.Report(L, await SubService.RemoveSubstitutionAsync(sub.Id),
            L["Substitution undone"], Severity.Warning);

    private async Task EditSubstitution(GameSubstitution sub)
    {
        if (GameData is null || FindPlayer(sub.PlayerOffId) is not { } offPlayer) return;

        var candidates = CandidatesFor(GameData.Periods.FirstOrDefault(p => p.Id == sub.GamePeriodId));
        if (FindPlayer(sub.PlayerOnId) is { } on && candidates.All(p => p.Id != on.Id))
            candidates = [on, .. candidates];

        var shownMinute = MatchClockReport.MinuteOf(GameData, sub).Minute;

        var choice = await DialogService.PromptAsync<EditSubDialog, EditSubChoice>(
            L["Edit substitution"],
            p =>
            {
                p.Add(x => x.PlayerOff, offPlayer);
                p.Add(x => x.Candidates, candidates);
                p.Add(x => x.PlayerOnId, sub.PlayerOnId);
                p.Add(x => x.Minute, shownMinute);
                p.Add(x => x.MaxMinute, GameData.GameDurationMinutes);
            });
        if (choice is null) return;

        Snackbar.Report(L,
            await SubService.EditSubstitutionAsync(
                sub.Id, sub.PlayerOffId, choice.PlayerOnId,
                MatchClockReport.ElapsedForEditedMinute(GameData, sub, shownMinute, choice.Minute)),
            L["Substitution updated"]);
    }

    private async Task RemoveInjury(GameInjury injury) =>
        Snackbar.Report(L, await SubService.RemoveInjuryAsync(injury.Id),
            L["Injury undone"], Severity.Warning);

    private async Task OpenSubDialog(int playerId)
    {
        if (!CanSubstitute) return;

        var tapped = OnPitch.FirstOrDefault(p => p.PlayerId == playerId);
        if (tapped is null || FindPlayer(playerId) is not { } player) return;

        var choice = await DialogService.PromptAsync<LiveSubDialog, LiveSubChoice>(
            L["Substitution"],
            p =>
            {
                p.Add(x => x.Player, player);
                p.Add(x => x.Position, tapped.Position);
                p.Add(x => x.Bench, SubCandidates);
                p.Add(x => x.OnPitch, IsAtBreak ? [] : SwapCandidates(playerId));
                p.Add(x => x.AllowSwapAndInjury, !IsAtBreak);
            });
        if (choice is null) return;

        // At the break the dialog offers only who comes on, so the choice is always a straight change to the next half's line-up — recorded
        // as a substitution when the half kicks off, not now.
        if (IsAtBreak)
        {
            var planned = await SubService.PlanBreakSubstitutionAsync(GameId, playerId, choice.PlayerId!.Value);
            Snackbar.Report(L, planned, L["Half-time change made"]);
            return;
        }

        if (choice.IsPositionSwap)
        {
            var swap = await SubService.SwapPositionsAsync(GameId, playerId, choice.PlayerId!.Value);
            Snackbar.Report(L, swap, L["Positions swapped"]);
            return;
        }

        // One call for both: the injury takes her off and any replacement comes on in one write.
        if (choice.IsInjury)
        {
            var injured = await SubService.MarkInjuredAsync(GameId, playerId, choice.PlayerId);
            Snackbar.Report(L, injured, L["{0} is off injured", player.ShortName], Severity.Warning);
            return;
        }

        var sub = await SubService.SubstituteAsync(GameId, playerId, choice.PlayerId!.Value);
        Snackbar.Report(L, sub, L["Substitution made"]);
    }

    /// Slot order, so the list reads like a team sheet rather than in whatever order the line-up rows were stored.
    private List<PitchPlayer> SwapCandidates(int playerId) =>
        [.. OnPitch
            .Where(p => p.PlayerId != playerId)
            .OrderBy(p => p.SlotIndex)
            .Select(p => (Entry: p, Player: FindPlayer(p.PlayerId)))
            .Where(x => x.Player is not null)
            .Select(x => new PitchPlayer(x.Player!, x.Entry.Position))];

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

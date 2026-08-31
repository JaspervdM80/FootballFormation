using System.Timers;
using FootballFormation.Core.Reporting;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Pages;

/// Sorted on <paramref name="AtSeconds"/> first: the elapsed clock runs on across the break, so a first-half stoppage entry stays above
/// the restart without comparing scoreboard readings. <paramref name="Minute"/> is that scoreboard reading, display only.
public record MatchEvent(
    int AtSeconds, MatchMinute? Minute, PeriodType Half, DateTime RecordedAt, int Id,
    GameGoal? Goal, GameSubstitution? Substitution, GameInjury? Injury = null,
    MatchScore? Score = null, bool HalfTimeAbove = false);

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

    private GamePeriod? DisplayHalf => GameData?.CurrentOrLastHalf();

    private bool IsHalfInPlay => GameData?.LivePeriodId is not null;

    private List<GamePlayerPosition> DisplayLineup => DisplayHalf?.PlayerPositions ?? [];

    private List<GamePlayerPosition> OnPitch => [.. DisplayLineup.Where(p => !p.IsSubstitute)];

    /// A line-up outlives the roster it was built from, so the roster filter is what stops the bench offering someone who is not at the match.
    private List<GamePlayerPosition> OnBench =>
        [.. DisplayLineup.Where(p => p.IsSubstitute && IsInRoster(p.PlayerId))];

    private bool IsInRoster(int playerId) =>
        GameData is not null && FindPlayer(playerId) is { } player && GameData.IsInRoster(player, Squad);

    private FormationType DisplayFormation =>
        DisplayHalf?.FormationTypeOverride ?? GameData?.FormationType ?? FormationType.F442;

    private bool CanSubstitute => _isAdmin && IsHalfInPlay;

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

    /// Includes roster players with no line-up entry at all, so a late arrival is not locked out of a match already under way. The two
    /// exclusions on top of <see cref="Game.SelectRoster"/> are a standing injury and one picked up in this match.
    private List<Player> SubCandidates
    {
        get
        {
            if (GameData is null) return [];

            var hurt = GameData.Injuries.Select(i => i.PlayerId).ToHashSet();
            var inLineup = DisplayLineup.Select(p => p.PlayerId).ToHashSet();
            var bench = OnBench.Select(p => FindPlayer(p.PlayerId)).OfType<Player>();
            var unlisted = GameData.SelectRoster(AllPlayers, Squad)
                .Where(p => !inLineup.Contains(p.Id) && !Squad.IsInjured(p.Id));

            return [.. bench.Concat(unlisted).Where(p => !hurt.Contains(p.Id))];
        }
    }

    /// Recomputed on every render, which is what keeps a playing total climbing with the clock.
    private List<LiveMinutesRow> MinutesPlayed =>
        GameData is null ? [] : LiveMinutesReport.Build(GameData, ElapsedSeconds, FindPlayer);

    /// False before kick-off, when <see cref="MinutesPlayed"/> is the planned line-up costed at a full period each — a different thing,
    /// and the card has to say so rather than claim minutes nobody has played.
    private bool MinutesAreActual => GameData?.HasActualTimings == true;

    /// Counts only the two kinds <see cref="ShowSubstitutions"/> can hide, so an empty timeline can be told apart from a filtered one.
    private bool HasEvents => GameData is { } game && (game.Goals.Count > 0 || game.Substitutions.Count > 0);

    /// Substitutions can be filtered out because heavy rotation buries the goals among them; an injury is never folded away.
    private List<MatchEvent> Timeline
    {
        get
        {
            if (GameData is null) return [];

            // Counted forwards over the whole match, then looked up per goal — this list runs newest first, so accumulating while
            // rendering it would count down.
            var progression = ScoreProgressionReport.Build(GameData);

            var goals = GameData.Goals.Select(g =>
            {
                var at = MatchClockReport.ElapsedOf(GameData, g);
                return new MatchEvent(
                    at,
                    MatchClockReport.MinuteOf(GameData, g),
                    MatchClockReport.HalfOf(GameData, g.GamePeriodId, at),
                    g.RecordedAt, g.Id, g, null, Score: progression[g.Id]);
            });
            IEnumerable<MatchEvent> subs = ShowSubstitutions
                ? GameData.Substitutions.Select(s => new MatchEvent(
                    s.AtSeconds,
                    MatchClockReport.MinuteOf(GameData, s),
                    MatchClockReport.HalfOf(GameData, s.GamePeriodId, s.AtSeconds),
                    s.RecordedAt, s.Id, null, s, GameData.InjuryFor(s)))
                : [];

            // Only the injuries nobody came on for; the rest are on their substitution's line.
            var injuries = GameData.Injuries
                .Where(i => !GameData.WasReplaced(i))
                .Select(i => new MatchEvent(
                    i.AtSeconds,
                    MatchClockReport.MinuteOf(GameData, i),
                    MatchClockReport.HalfOf(GameData, i.GamePeriodId, i.AtSeconds),
                    i.RecordedAt, i.Id, null, null, i));

            // A goal and the sub that followed it commonly share a second, so the entry time orders them as they happened. The id then
            // settles a double substitution, keeping the top entry the one RemoveSubstitutionAsync will undo.
            var ordered = goals.Concat(subs).Concat(injuries)
                .OrderByDescending(e => e.AtSeconds)
                .ThenByDescending(e => e.RecordedAt)
                .ThenByDescending(e => e.Id)
                .ToList();

            // Marked here rather than in the markup, which renders one entry at a time and cannot see the one above it.
            return [.. ordered.Select((e, i) =>
                i > 0 && ordered[i - 1].Half != e.Half ? e with { HalfTimeAbove = true } : e)];
        }
    }

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

    private async Task RemoveInjury(GameInjury injury) =>
        Snackbar.Report(L, await SubService.RemoveInjuryAsync(injury.Id),
            L["Injury undone"], Severity.Warning);

    /// The cross wins over the swap arrows on a substitution made for an injury: what happened there was the injury.
    private static string EventIcon(MatchEvent entry) => entry switch
    {
        { Goal: not null } => Icons.Material.Filled.SportsSoccer,
        { Injury: not null } => Icons.Material.Filled.MedicalServices,
        _ => Icons.Material.Filled.SwapHoriz
    };

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
                p.Add(x => x.OnPitch, SwapCandidates(playerId));
            });
        if (choice is null) return;

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

    /// <see cref="MatchScore"/> is always ours first, so it needs the same venue flip the scoreboard above the timeline uses.
    private string ScoreText(MatchScore score) => GameData?.IsHomeGame == false
        ? $"{score.Them}–{score.Us}"
        : $"{score.Us}–{score.Them}";

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

using System.Timers;
using FootballFormation.Core.Reporting;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Pages;

/// <summary>
/// One entry on the match timeline — a goal, a substitution or an injury — so all three can be
/// listed together.
/// <para>
/// <paramref name="Injury"/> is set alongside <paramref name="Substitution"/> when the player who
/// came off was hurt: one tap wrote both rows, so they get one line. It stands alone only when
/// nobody came on for her.
/// </para>
/// <para>
/// Sorted on <paramref name="AtSeconds"/>, then <paramref name="RecordedAt"/>, then
/// <paramref name="Id"/>. The elapsed clock runs on across the break, so a first-half stoppage
/// entry stays above the restart without anyone comparing scoreboard readings;
/// <paramref name="Minute"/> is that scoreboard reading, display only. The last two settle ties:
/// entries entered in one instant share a <paramref name="RecordedAt"/>, and rows older than that
/// column all read <c>0001-01-01</c>. Across the two kinds the ids come from different tables, so
/// a tie there is arbitrary — but stable, which is what the list needs.
/// </para>
/// <para>
/// <paramref name="HalfTimeAbove"/> marks the one entry the break is drawn above, which only a
/// neighbour can decide.
/// </para>
/// </summary>
public record MatchEvent(
    int AtSeconds, MatchMinute? Minute, PeriodType Half, DateTime RecordedAt, int Id,
    GameGoal? Goal, GameSubstitution? Substitution, GameInjury? Injury = null,
    MatchScore? Score = null, bool HalfTimeAbove = false);

/// <summary>
/// The sideline screen. An admin runs the clock and records what happens; everyone else sees the
/// same page read-only and updating live, so there is one URL to share during a match.
/// </summary>
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

    /// <summary>
    /// Whether the timeline lists substitutions alongside the goals. Per circuit and not stored:
    /// it is a glance-vs-detail choice made in the moment, and it survives the live reloads
    /// because those replace the data rather than the component.
    /// </summary>
    private bool ShowSubstitutions { get; set; } = true;

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
        : MatchClockReport.Build(GameData, DisplayHalf, ElapsedSeconds);

    private static string Mmss(int seconds) => $"{seconds / 60:D2}:{seconds % 60:D2}";

    private string ClockDisplay => Mmss(Clock.Seconds);

    private string AdditionalDisplay => Mmss(Clock.AdditionalSeconds);

    /// <summary>
    /// The half on screen, as the line-up it is played with: the one being played; at half time and
    /// after the whistle the last one that was; and before kick-off the half the match opens with,
    /// so the pitch is never blank when a line-up exists.
    /// </summary>
    private GamePeriod? DisplayHalf => GameData?.CurrentOrLastHalf();

    private bool IsHalfInPlay => GameData?.LivePeriodId is not null;

    private List<GamePlayerPosition> DisplayLineup => DisplayHalf?.PlayerPositions ?? [];

    private List<GamePlayerPosition> OnPitch => [.. DisplayLineup.Where(p => !p.IsSubstitute)];

    /// <summary>
    /// The bench for this half. A lineup can outlive the roster it was built from — someone
    /// marked unavailable, or dropped from the squad, keeps their saved substitute row — and
    /// listing them as a sub would offer a player who is not at the match.
    /// </summary>
    private List<GamePlayerPosition> OnBench =>
        [.. DisplayLineup.Where(p => p.IsSubstitute && IsInRoster(p.PlayerId))];

    private bool IsInRoster(int playerId) =>
        GameData is not null && FindPlayer(playerId) is { } player && GameData.IsInRoster(player, Squad);

    private FormationType DisplayFormation =>
        DisplayHalf?.FormationTypeOverride ?? GameData?.FormationType ?? FormationType.F442;

    private bool CanSubstitute => _isAdmin && IsHalfInPlay;

    private GamePeriod? NextHalf => GameData?.NextHalf();

    /// <summary>
    /// The half on the clock, which is the only division this screen names. Quarters exist to plan
    /// two line-ups per half; nobody standing at the pitch thinks in them, so the second line-up of
    /// a half never appears here as a stage of the match — only behind
    /// <see cref="ShowPlannedChanges"/>, as a plan to work through.
    /// </summary>
    private string? DisplayHalfLabel => DisplayHalf?.PeriodType.HalfDisplayName();

    private string? NextHalfLabel => NextHalf?.PeriodType.HalfDisplayName();

    /// <summary>
    /// Whether whistling the half off leads to half time rather than to the end of the match. A
    /// match is two halves, so before full time there is exactly one stoppage — and after it
    /// <see cref="NextHalf"/> is null and the only control left is the final whistle.
    /// </summary>
    private bool HalfTimeFollows => IsHalfInPlay && NextHalf is not null;

    /// <summary>
    /// The swaps the planned line-ups imply for the middle of this half, measured against who is
    /// on the pitch right now — so a live substitution already made drops out of the list. They are
    /// carried out by hand, one tap on the pitch at a time; nothing here rolls them on at once.
    /// <para>
    /// Looked up from the planned line-ups rather than from the clock, so the changes due can be
    /// read before kick-off as well as during play. Empty once the match is over — there is
    /// nothing left to plan for.
    /// </para>
    /// </summary>
    private PlannedChanges PlannedChanges =>
        GameData is { MatchState: not MatchState.Finished } game
        && DisplayHalf is { } half
        && game.MidHalfPlan(half) is { } plan
            ? PlannedChangesReport.Build(half, plan, FindPlayer,
                game.Substitutions.Where(s => s.GamePeriodId == half.Id))
            : PlannedChanges.None;

    /// <summary>
    /// How many changes the plan still holds. It is what the button opening the plan says, and
    /// whether it is shown at all — a count is enough to know if the tap is worth making.
    /// </summary>
    private int PlannedChangeCount =>
        PlannedChanges.Substitutions.Count + PlannedChanges.Moves.Count;

    /// <summary>What the match is doing right now, in one phrase under the clock.</summary>
    private string StatusLabel => GameData?.MatchState switch
    {
        null or MatchState.NotStarted => L["Not started"],
        MatchState.Finished => L["Full time"],
        _ when !IsHalfInPlay => L["Half time"],
        // The half is played out and play has not stopped — the thing to say is how much longer.
        _ when Clock.IsInAdditionalTime => L["Additional time"],
        _ => L[DisplayHalfLabel ?? "In progress"]
    };

    /// <summary>
    /// Drives the colour of the status chip. A half being played always has a running clock —
    /// nothing stops one short of the whistle — so the third arm here is half time.
    /// </summary>
    private string StatusCssClass => GameData?.MatchState switch
    {
        MatchState.InProgress when GameData.IsClockRunning && Clock.IsInAdditionalTime =>
            "live-status live-status-extra",
        MatchState.InProgress when GameData.IsClockRunning => "live-status live-status-running",
        MatchState.InProgress => "live-status live-status-break",
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
    /// Who can come on: the bench for this half, plus anyone in the roster with no lineup entry
    /// at all — a late arrival should not be locked out of a match already under way. Two
    /// exclusions on top of <see cref="Game.SelectRoster"/>: a player generally injured, the same
    /// one <c>FormationBuilder</c> applies; and anyone already hurt in this match.
    /// </summary>
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

    /// <summary>
    /// Exact minutes on the pitch so far, for the admin deciding who to bring on next. Recomputed
    /// on every render, which is what keeps the running player's total climbing with the clock.
    /// </summary>
    private List<LiveMinutesRow> MinutesPlayed =>
        GameData is null ? [] : LiveMinutesReport.Build(GameData, ElapsedSeconds, FindPlayer);

    /// <summary>
    /// Whether those minutes are time actually played. Until the first kick-off there is none, and
    /// the figures are the planned line-up costed at a full period each — a different thing, which
    /// is why the card is headed differently rather than claiming minutes nobody has played yet.
    /// </summary>
    private bool MinutesAreActual => GameData?.HasActualTimings == true;

    /// <summary>
    /// Whether an empty timeline is the checkbox's doing rather than the truth. Only the two kinds
    /// it can hide are counted — an injury is always listed.
    /// </summary>
    private bool HasEvents => GameData is { } game && (game.Goals.Count > 0 || game.Substitutions.Count > 0);

    /// <summary>
    /// Goals, substitutions and injuries on one timeline, most recent first. Substitutions can be
    /// left out: a match with a lot of rotation buries the goals among them, and the goals are what
    /// someone scrolling back is usually after. An injury is never folded away.
    /// </summary>
    private List<MatchEvent> Timeline
    {
        get
        {
            if (GameData is null) return [];

            // Counted forwards over the whole match, then looked up per goal: this list runs
            // newest first, so a total accumulated while rendering it would count down.
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

            // A goal and the sub that followed it commonly share a second; the entry time keeps
            // them in the order they actually happened rather than the order they were queried.
            // The id then settles a double substitution, so the entry this list shows on top is
            // the one MatchSubstitutionService.RemoveSubstitutionAsync will let an admin undo.
            var ordered = goals.Concat(subs).Concat(injuries)
                .OrderByDescending(e => e.AtSeconds)
                .ThenByDescending(e => e.RecordedAt)
                .ThenByDescending(e => e.Id)
                .ToList();

            // Where the second half's events give way to the first's, reading down a list that
            // runs newest first. Marked here because the markup renders one entry at a time and
            // cannot see the one above it — and because the filter above decides who the
            // neighbours are.
            return [.. ordered.Select((e, i) =>
                i > 0 && ordered[i - 1].Half != e.Half ? e with { HalfTimeAbove = true } : e)];
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

    /// <summary>Only repaints while the clock is actually moving — at a break there is nothing to redraw.</summary>
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
        Snackbar.Report(L, await ClockService.StartMatchAsync(GameId), L["Match started"]);

    private async Task EndHalf() =>
        Snackbar.Report(L, await ClockService.EndHalfAsync(GameId), L["Half ended"], Severity.Info);

    private async Task StartNextHalf() =>
        Snackbar.Report(L, await ClockService.StartNextHalfAsync(GameId), L["Next half started"]);

    /// <summary>Opens the plan for the middle of this half. Nothing here writes anything.</summary>
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

    /// <summary>The cross wins over the swap arrows on a substitution made for an injury: what
    /// happened there was the injury.</summary>
    private static string EventIcon(MatchEvent entry) => entry switch
    {
        { Goal: not null } => Icons.Material.Filled.SportsSoccer,
        { Injury: not null } => Icons.Material.Filled.MedicalServices,
        _ => Icons.Material.Filled.SwapHoriz
    };

    /// <summary>
    /// Tapping a player on the pitch asks what happens to them: someone comes on for them, they
    /// trade positions with a team-mate who stays on, or they go off hurt.
    /// </summary>
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

    /// <summary>
    /// Who the tapped player can trade positions with: the rest of the pitch, in slot order so the
    /// list reads like a team sheet rather than in whatever order the lineup rows were stored.
    /// </summary>
    private List<PitchPlayer> SwapCandidates(int playerId) =>
        [.. OnPitch
            .Where(p => p.PlayerId != playerId)
            .OrderBy(p => p.SlotIndex)
            .Select(p => (Entry: p, Player: FindPlayer(p.PlayerId)))
            .Where(x => x.Player is not null)
            .Select(x => new PitchPlayer(x.Player!, x.Entry.Position))];

    /// <summary>
    /// A scoreline for the timeline, in the same order as the scoreboard above it — the home side
    /// on the left, whoever that is. <see cref="MatchScore"/> itself is always ours first.
    /// </summary>
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

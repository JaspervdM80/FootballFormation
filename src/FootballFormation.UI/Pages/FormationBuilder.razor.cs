using FootballFormation.Core.Reporting;
using FootballFormation.UI.Components;
using Microsoft.Extensions.Logging;

namespace FootballFormation.UI.Pages;

public partial class FormationBuilder
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private LineupService LineupService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private StatsService StatsService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private ILogger<FormationBuilder> Logger { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private Game? PreviousLineup { get; set; }
    private List<Player>? AllPlayers { get; set; }

    /// From the game, not the season picker: the builder is scoped to one fixture and must not follow a global filter.
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;
    private Dictionary<int, List<GamePlayerPosition>> PeriodLineups { get; } = [];
    private LineupDragState Drag { get; } = new();

    /// Each period's line-up as it was last loaded or saved. Compared by value, because a drop moves the same entry objects around.
    private Dictionary<int, string> SavedLineups { get; } = [];

    private int ActivePeriodIndex
    {
        get;
        set
        {
            // A selection names a slot in the period on screen, so it cannot follow the coach to another one.
            if (field != value) Drag.Clear();
            field = value;
        }
    }

    private Player? SelectedPlayer =>
        Drag.SelectedPlayerId is { } id ? AllPlayers?.FirstOrDefault(p => p.Id == id) : null;

    protected override async Task OnInitializedAsync()
    {
        var gameResult = await GameService.GetByIdAsync(GameId, Cancellation);

        // Not a missing game — the visitor left. Redirecting would move them again.
        if (gameResult.IsCancelled) return;

        if (!Snackbar.ReportFailure(L, gameResult))
        {
            Logger.LogWarning("Game {GameId} not found, redirecting to games list", GameId);
            Trail.Redirect(AppRoutes.Games);
            return;
        }

        GameData = gameResult.Value!;

        var people = await SquadService.GetMatchPeopleAsync(GameData.SeasonId, Cancellation);
        (Squad, AllPlayers) = Snackbar.ReportFailure(L, people) ? people.Value! : MatchPeople.None;

        CacheLineups();

        if (CanCopyPreviousLineupAt(GameData))
        {
            var previousResult = await LineupService.GetPreviousFullLineupAsync(GameId, Cancellation);
            PreviousLineup = Snackbar.ReportFailure(L, previousResult) ? previousResult.Value : null;
        }

        Logger.LogDebug("Loaded formation builder for game {GameId} vs {Opponent}",
            GameId, GameData.Opponent);
    }

    /// A played half is left out: the touchline owns it, and the save skips it.
    private bool HasUnsavedChanges =>
        GameData is not null && !GameData.IsComplete
        && GameData.Periods.Any(p => !p.HasKickedOff
            && PeriodLineups.TryGetValue(p.Id, out var lineup)
            && LineupSignature(lineup) != SavedLineups.GetValueOrDefault(p.Id));

    private static string LineupSignature(IEnumerable<GamePlayerPosition> lineup) =>
        string.Join(';', lineup
            .OrderBy(p => p.PlayerId)
            .Select(p => $"{p.PlayerId}:{p.IsSubstitute}:{p.SlotIndex}:{p.Position}"));

    private void RememberSavedLineups()
    {
        SavedLineups.Clear();
        foreach (var (periodId, lineup) in PeriodLineups)
            SavedLineups[periodId] = LineupSignature(lineup);
    }

    /// Injury is filtered here rather than in <see cref="Game.IsInRoster"/>, which also judges games already played — where a status set
    /// after the fact must not rewrite what happened.
    private SelectionPanels Panels =>
        AllPlayers is null || GameData is null ? SelectionPanels.Empty : GameData.SplitForSelection(AllPlayers, Squad);

    private List<Player> RosterPlayers => Panels.Selectable;

    private List<Player> UnavailablePlayers => Panels.Unavailable;

    private List<Player> InjuredPlayers => Panels.Injured;

    /// Every editing handler below stops here: the touchline owns a kicked-off half, and a finished match is a record.
    private bool IsLocked(int periodId) =>
        GameData!.IsComplete || GameData.Periods.First(p => p.Id == periodId).HasKickedOff;

    private static bool CanCopyPreviousLineupAt(Game game) =>
        !game.IsComplete && !game.Periods.Any(p => p.HasKickedOff);

    private bool CanCopyPreviousLineup =>
        PreviousLineup is not null && GameData is not null && CanCopyPreviousLineupAt(GameData);

    private List<Player> GetAvailablePlayers(int periodId)
    {
        var usedIds = PeriodLineups.TryGetValue(periodId, out var lineup)
            ? lineup.Select(p => p.PlayerId).ToHashSet()
            : [];

        return RosterPlayers.Where(p => !usedIds.Contains(p.Id)).ToList();
    }

    private PlayerPosition[] GetAllSlots(int periodId)
    {
        var period = GameData!.Periods.First(p => p.Id == periodId);
        return FormationSlots.For(period.FormationTypeOverride ?? GameData.FormationType);
    }

    /// Who is standing where in this period, using the same rule the pitch draws with.
    private GamePlayerPosition?[] BuildSlotAssignments(int periodId) =>
        FormationSlots.Assign(GetAllSlots(periodId), PeriodLineups.GetValueOrDefault(periodId, []));

    private LineupEditor EditorFor(int periodId) => new(PeriodLineups[periodId], GetAllSlots(periodId));

    private (int Filled, int Slots) PeriodFill(int periodId)
    {
        var assignments = BuildSlotAssignments(periodId);
        return (assignments.Count(a => a is not null), assignments.Length);
    }

    private void OnPlayerDragStart(int playerId) => Drag.StartFromList(playerId);

    private void OnSubDragStart(int playerId) => Drag.StartFromSub(playerId);

    private void OnPitchPlayerDragStart(int periodId, int slotIndex)
    {
        if (IsLocked(periodId)) return;

        var existing = BuildSlotAssignments(periodId)[slotIndex];
        if (existing is null) return;

        Drag.StartFromPitch(existing.PlayerId, slotIndex);
    }

    private void OnPlayerDropped(int periodId, int slotIndex)
    {
        if (Drag.PlayerId is null || AllPlayers is null || IsLocked(periodId)) return;

        if (Drag.FromSlotIndex is { } sourceSlotIndex)
            EditorFor(periodId).MoveOnPitch(sourceSlotIndex, slotIndex);
        else if (AllPlayers.FirstOrDefault(p => p.Id == Drag.PlayerId) is { } player)
            EditorFor(periodId).PutOnPitch(player, slotIndex, fromBench: Drag.FromSub);

        Drag.Clear();
        StateHasChanged();
    }

    private void OnPlayerDroppedToSub(int periodId)
    {
        if (Drag.PlayerId is null || AllPlayers is null || IsLocked(periodId)) return;

        if (AllPlayers.FirstOrDefault(p => p.Id == Drag.PlayerId) is { } player)
            EditorFor(periodId).PutOnBench(player);

        Drag.Clear();
        StateHasChanged();
    }

    /// Drop of a dragged starter onto a bench player: the two trade places.
    private void OnSwapFieldPlayerWithSub(int periodId, int subPlayerId)
    {
        if (Drag.PlayerId is null || Drag.PlayerId == subPlayerId || IsLocked(periodId)) return;
        if (Drag.FromSlotIndex is not { } slotIndex)
        {
            // A sub row covers most of the bench panel, and it stops the drop from reaching it — so a
            // player dragged from the list onto one joins the bench rather than landing in nothing.
            if (!Drag.FromSub) OnPlayerDroppedToSub(periodId);
            return;
        }

        if (!EditorFor(periodId).SwapWithSub(Drag.PlayerId.Value, slotIndex, subPlayerId)) return;

        Drag.Clear();
        StateHasChanged();
    }

    private void OnListPlayerTapped(int playerId)
    {
        if (Drag.SelectedPlayerId == playerId)
            Drag.Clear();
        else
            Drag.StartFromList(playerId, tapped: true);
    }

    /// With nothing in hand a tap picks the chip up; with a player in hand it places them, exactly as a drop would.
    private void OnSlotTapped(int periodId, int slotIndex)
    {
        if (IsLocked(periodId)) return;

        if (!Drag.IsTapSelection)
        {
            if (BuildSlotAssignments(periodId)[slotIndex] is { } occupant)
                Drag.StartFromPitch(occupant.PlayerId, slotIndex, tapped: true);
            else
                Drag.Clear();
            return;
        }

        if (Drag.FromSlotIndex == slotIndex)
        {
            Drag.Clear();
            return;
        }

        OnPlayerDropped(periodId, slotIndex);
    }

    private void OnSubTapped(int periodId, int subPlayerId)
    {
        if (IsLocked(periodId)) return;

        if (Drag.IsTapSelection && !Drag.FromSub)
        {
            OnSwapFieldPlayerWithSub(periodId, subPlayerId);
            return;
        }

        if (Drag.SelectedPlayerId == subPlayerId)
            Drag.Clear();
        else
            Drag.StartFromSub(subPlayerId, tapped: true);
    }

    private void OnBenchTapped(int periodId)
    {
        if (Drag.IsTapSelection && !Drag.FromSub)
            OnPlayerDroppedToSub(periodId);
    }

    private void BenchSelected(int periodId)
    {
        if (IsLocked(periodId) || Drag.SelectedSlotIndex is not { } slotIndex) return;

        EditorFor(periodId).BenchAt(slotIndex);
        Drag.Clear();
    }

    private void RemoveSelected(int periodId)
    {
        if (IsLocked(periodId) || Drag.PlayerId is not { } playerId) return;

        EditorFor(periodId).Remove(playerId);
        Drag.Clear();
    }

    private void RemoveSub(int periodId, GamePlayerPosition sub)
    {
        if (IsLocked(periodId)) return;

        PeriodLineups[periodId].Remove(sub);
        if (Drag.SelectedPlayerId == sub.PlayerId) Drag.Clear();
    }

    /// The shape belongs to the game, so it saves on the spot. The service reshapes the line-ups it holds; the same is done here to the
    /// copy on the page, so a drag not yet saved survives the switch instead of being reloaded away.
    private async Task OnFormationChanged(FormationType formation)
    {
        if (GameData is null || formation == GameData.FormationType) return;

        var result = await LineupService.SaveFormationAsync(GameId, formation);
        if (!Snackbar.ReportFailure(L, result)) return;

        // The service reshaped every unplayed half the same way, so a page with nothing pending still has nothing pending.
        var hadUnsavedChanges = HasUnsavedChanges;
        ReshapeCachedLineups(formation);
        if (!hadUnsavedChanges) RememberSavedLineups();
        Drag.Clear();
        Snackbar.Add(L["Formation changed to {0}", L[formation.DisplayName()].Value], Severity.Success);
    }

    private void ReshapeCachedLineups(FormationType formation)
    {
        var slots = FormationSlots.For(formation);

        foreach (var period in GameData!.Periods)
        {
            FormationSlots.Reshape(PeriodLineups[period.Id], GetAllSlots(period.Id), slots);
            period.FormationTypeOverride = null;
        }

        // Last: GetAllSlots above falls back to it for the shape being left.
        GameData.FormationType = formation;
    }

    private bool IsLastPeriodSelected =>
        GameData is not null && ActivePeriodIndex >= GameData.Periods.Count - 1;

    private bool CanCopyToNextPeriod =>
        GameData is not null && !IsLastPeriodSelected
        && !IsLocked(GameData.Periods.OrderBy(p => p.PeriodType).ToList()[ActivePeriodIndex + 1].Id);

    private void CopyToNextPeriod()
    {
        if (!CanCopyToNextPeriod) return;

        var orderedPeriods = GameData!.Periods.OrderBy(p => p.PeriodType).ToList();
        var sourcePeriod = orderedPeriods[ActivePeriodIndex];
        var nextPeriod = orderedPeriods[ActivePeriodIndex + 1];

        PeriodLineups[nextPeriod.Id] = LineupEditor.Copy(PeriodLineups[sourcePeriod.Id]);

        ActivePeriodIndex++;

        Logger.LogInformation("Copied lineup from {SourcePeriod} to {NextPeriod} for game {GameId}",
            sourcePeriod.PeriodType, nextPeriod.PeriodType, GameId);
        Snackbar.Add(L["Lineup copied to {0}", L[nextPeriod.PeriodType.DisplayName()].Value], Severity.Info);
    }

    private async Task CopyPreviousLineup()
    {
        if (!CanCopyPreviousLineup || AllPlayers is null) return;

        var source = PreviousLineup!;

        var changesFormation = source.FormationType != GameData!.FormationType;

        // A formation change saves on the spot, so the dialog has to say so even with nobody set up yet.
        if ((changesFormation || PeriodLineups.Values.Any(lineup => lineup.Count > 0))
            && !await DialogService.ConfirmAsync(
                L["Copy the line-up from {0}?", source.Opponent],
                changesFormation
                    ? L["The formation changes to {0} straight away, and anyone already set up is replaced. The line-up itself is not saved until you save.",
                        L[source.FormationType.DisplayName()].Value]
                    : L["Everyone currently set up for this match is replaced. Nothing is saved until you save."],
                "Replace"))
        {
            return;
        }

        if (changesFormation)
        {
            await OnFormationChanged(source.FormationType);
            if (GameData.FormationType != source.FormationType) return;
        }

        var available = RosterPlayers.Select(p => p.Id).ToHashSet();
        foreach (var (periodId, lineup) in GameData.LineupsCopiedFrom(source, available))
            PeriodLineups[periodId] = lineup;

        Drag.Clear();

        var leftOut = source.Periods
            .SelectMany(p => p.PlayerPositions)
            .Where(pp => !available.Contains(pp.PlayerId))
            .Select(pp => pp.Player?.DisplayName ?? "?")
            .Distinct()
            .ToList();

        Logger.LogInformation("Copied the line-up of game {SourceGameId} into game {GameId}", source.Id, GameId);
        Snackbar.Add(
            leftOut.Count == 0
                ? L["Line-up copied from {0} — check it before saving.", source.Opponent]
                : L["Line-up copied from {0}, leaving out who is not available for this match: {1}. Check it before saving.",
                    source.Opponent, string.Join(", ", leftOut)],
            Severity.Success);
    }

    private async Task SuggestLineup(int periodId)
    {
        if (GameData is null || IsLocked(periodId)) return;

        if (PeriodLineups[periodId].Count > 0
            && !await DialogService.ConfirmAsync(
                L["Suggest a line-up?"],
                L["Everyone currently set up for this part of the match is replaced. Nothing is saved until you save."],
                "Replace"))
        {
            return;
        }

        var minutes = await MinutesPlayedAsync(periodId);
        if (minutes is null) return;

        var suggestion = LineupSuggestionReport.Build(GetAllSlots(periodId), RosterPlayers, minutes);

        Drag.Clear();
        PeriodLineups[periodId] =
        [
            .. suggestion.Starters.Select(s => LineupEditor.Starter(s.Player, s.Position, s.SlotIndex)),
            .. suggestion.Substitutes.Select(LineupEditor.Substitute)
        ];

        Logger.LogInformation("Suggested a lineup for period {PeriodId} of game {GameId}", periodId, GameId);
        Snackbar.Add(L["Suggested a line-up — check it before saving."], Severity.Success);
    }

    /// Season so far plus the other periods of this match as planned, so a second half balances the first. Null when the season figures
    /// could not be read.
    private async Task<Dictionary<int, int>?> MinutesPlayedAsync(int periodId)
    {
        var seasonResult = await StatsService.GetSeasonAsync(GameData!.SeasonId, Cancellation);
        if (!Snackbar.ReportFailure(L, seasonResult)) return null;

        var minutes = seasonResult.Value!.Stats.Players
            .ToDictionary(p => p.Player.Id, p => p.TotalMinutes);

        // A finished match is already in the season figures, periods and all, so adding them again would bench the very players the
        // line-up beside this one just picked.
        if (GameData.IsComplete) return minutes;

        var planned = new Dictionary<int, int>();

        foreach (var (otherPeriodId, lineup) in PeriodLineups.Where(entry => entry.Key != periodId))
        {
            var period = GameData.Periods.First(p => p.Id == otherPeriodId);
            var seconds = period.StartedAtSeconds is { } start && period.EndedAtSeconds is { } end
                ? end - start
                : GameData.PeriodDurationSeconds;

            foreach (var entry in lineup.Where(e => !e.IsSubstitute))
                planned[entry.PlayerId] = planned.GetValueOrDefault(entry.PlayerId) + seconds;
        }

        // Converted once, per docs/known_issues/domain.md.
        foreach (var (playerId, seconds) in planned)
            minutes[playerId] = minutes.GetValueOrDefault(playerId) + Game.SecondsToMinutes(seconds);

        return minutes;
    }

    private async Task SaveAll()
    {
        // The service decides what has been played, not this page's copy: it may have been open since before kick-off.
        var result = await LineupService.SaveLineupsAsync(GameId, PeriodLineups);
        if (result.IsFailure)
        {
            if (!result.IsCancelled) Snackbar.Add(L["Save failed: {0}", UiFeedback.Translate(L, result)], Severity.Error);
            return;
        }

        if (result.Value is { Count: > 0 } played)
        {
            Snackbar.Add(
                L["Left as the touchline recorded it: {0}",
                    string.Join(", ", played.Select(p => L[p.DisplayName()].Value))],
                Severity.Info);
        }

        Snackbar.Add(L["All lineups saved!"], Severity.Success);
        Logger.LogInformation("Saved all lineups for game {GameId}", GameId);
        RememberSavedLineups();

        // Reload so the cached entries carry the database-generated ids.
        var gameResult = await GameService.GetByIdAsync(GameId, Cancellation);
        if (gameResult.IsSuccess)
        {
            GameData = gameResult.Value!;
            CacheLineups();
        }
    }

    private void CacheLineups()
    {
        if (GameData is null) return;

        foreach (var period in GameData.Periods)
        {
            PeriodLineups[period.Id] = period.PlayerPositions.ToList();
        }

        RememberSavedLineups();
    }

    private List<PlayingTimeRow> GetPlayingTimeData()
    {
        if (GameData is null || AllPlayers is null) return [];

        // Roster plus anyone already placed in a line-up, so the table always accounts for the whole pitch.
        var linedUpIds = PeriodLineups.Values
            .SelectMany(lineup => lineup)
            .Select(p => p.PlayerId)
            .ToHashSet();
        var players = AllPlayers.Where(p => GameData.IsInRoster(p, Squad) || linedUpIds.Contains(p.Id));

        return PlayingTimeReport.Build(GameData, players, PeriodLineups);
    }

    /// The playing-time table colours its cells with the same five tiers as the pitch.
    private static string GetFitCssClass(PositionFit fit) => Pitch.FitCssClass(fit);

    private static Color GetTimeColor(double percentage) => percentage switch
    {
        >= 90 => Color.Success,
        >= 50 => Color.Info,
        >= 25 => Color.Warning,
        _ => Color.Error
    };
}

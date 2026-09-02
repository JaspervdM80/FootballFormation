using FootballFormation.Core.Reporting;
using FootballFormation.UI.Components;
using Microsoft.Extensions.Logging;

namespace FootballFormation.UI.Pages;

public partial class FormationBuilder
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private ILogger<FormationBuilder> Logger { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private List<Player>? AllPlayers { get; set; }

    /// From the game, not the season picker: the builder is scoped to one fixture and must not follow a global filter.
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;
    private Dictionary<int, List<GamePlayerPosition>> PeriodLineups { get; } = [];
    private int ActivePeriodIndex { get; set; }
    private LineupDragState Drag { get; } = new();

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

        var squadResult = await SquadService.GetSquadAsync(GameData.SeasonId, Cancellation);
        Squad = Snackbar.ReportFailure(L, squadResult) ? squadResult.Value! : SeasonSquad.Empty;

        // The full pool: a player lined up but since dropped from the squad must stay visible in the playing-time table.
        var playersResult = await PlayerService.GetAllAsync(Cancellation);
        AllPlayers = Snackbar.ReportFailure(L, playersResult) ? playersResult.Value! : [];

        CacheLineups();

        Logger.LogDebug("Loaded formation builder for game {GameId} vs {Opponent}",
            GameId, GameData.Opponent);
    }

    /// Injury is filtered here rather than in <see cref="Game.IsInRoster"/>, which also judges games already played — where a status set
    /// after the fact must not rewrite what happened.
    private List<Player> RosterPlayers =>
        AllPlayers is null || GameData is null
            ? []
            : GameData.SelectRoster(AllPlayers, Squad).Where(p => !IsInjured(p.Id)).ToList();

    /// Excludes anyone <see cref="InjuredPlayers"/> lists, even when they are also marked unavailable, so nobody appears in both panels.
    private List<Player> UnavailablePlayers
    {
        get
        {
            if (AllPlayers is null || GameData is null) return [];

            var unavailable = GameData.UnavailablePlayerIds.ToHashSet();
            return AllPlayers
                .Where(p => Squad.IsFullMember(p.Id) && unavailable.Contains(p.Id) && !IsInjured(p.Id))
                .ToList();
        }
    }

    /// Generally injured, as opposed to unavailable for this one fixture. Excluded from <see cref="RosterPlayers"/>, so never a drag target.
    private List<Player> InjuredPlayers =>
        AllPlayers is null ? [] : AllPlayers.Where(p => IsInjured(p.Id)).ToList();

    /// Flagged in the squad now, or recorded as having missed this match. Without the second half, a player since recovered would be in
    /// none of the three panels and simply vanish from a line-up someone came back to fix.
    private bool IsInjured(int playerId) =>
        Squad.IsInjured(playerId) || (GameData?.InjuredPlayerIds.Contains(playerId) ?? false);

    /// Every editing handler below stops here rather than piling up changes the save would drop — the touchline owns a half once it
    /// has kicked off, and GameService.SavePeriodLineupAsync refuses one.
    private bool HasBeenPlayed(int periodId) =>
        GameData!.Periods.First(p => p.Id == periodId).HasKickedOff;

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

    private void OnPlayerDragStart(int playerId) => Drag.StartFromList(playerId);

    private void OnSubDragStart(int playerId) => Drag.StartFromSub(playerId);

    private void OnPitchPlayerDragStart(int periodId, int slotIndex)
    {
        if (HasBeenPlayed(periodId)) return;

        var existing = BuildSlotAssignments(periodId)[slotIndex];
        if (existing is null) return;

        Drag.StartFromPitch(existing.PlayerId, slotIndex);
    }

    private void OnPlayerDropped(int periodId, int slotIndex)
    {
        if (Drag.PlayerId is null || AllPlayers is null || HasBeenPlayed(periodId)) return;

        var slots = GetAllSlots(periodId);
        var position = slots[slotIndex];
        var lineup = PeriodLineups[periodId];

        if (Drag.FromSlotIndex is { } sourceSlotIndex)
        {

            var assignments = BuildSlotAssignments(periodId);
            var source = assignments[sourceSlotIndex];
            var target = assignments[slotIndex];

            if (source is not null)
            {
                source.Position = position;
                source.SlotIndex = slotIndex;
                if (target is not null)
                {
                    target.Position = slots[sourceSlotIndex];
                    target.SlotIndex = sourceSlotIndex;
                }
            }
        }
        else if (AllPlayers.FirstOrDefault(p => p.Id == Drag.PlayerId) is { } player)
        {
            var wasFromSub = Drag.FromSub;
            lineup.RemoveAll(p => p.PlayerId == player.Id);

            // A drop from the bench sends the current occupant back to it; a drop from the list replaces them outright.
            var existingAtSlot = BuildSlotAssignments(periodId)[slotIndex];
            if (existingAtSlot is not null)
            {
                if (wasFromSub)
                {
                    SendToBench(existingAtSlot);
                }
                else
                {
                    lineup.Remove(existingAtSlot);
                }
            }

            lineup.Add(CreateEntry(player, position, slotIndex));
        }

        Drag.Clear();
        StateHasChanged();
    }

    private void OnPlayerDroppedToSub(int periodId)
    {
        if (Drag.PlayerId is null || AllPlayers is null || HasBeenPlayed(periodId)) return;

        if (AllPlayers.FirstOrDefault(p => p.Id == Drag.PlayerId) is { } player)
        {
            var lineup = PeriodLineups[periodId];
            lineup.RemoveAll(p => p.PlayerId == player.Id);
            lineup.Add(CreateEntry(player, player.PreferredPosition, slotIndex: null, isSubstitute: true));
        }

        Drag.Clear();
        StateHasChanged();
    }

    /// Drop of a dragged starter onto a bench player: the two trade places.
    private void OnSwapFieldPlayerWithSub(int periodId, int subPlayerId)
    {
        if (Drag.PlayerId is null || Drag.PlayerId == subPlayerId || HasBeenPlayed(periodId)) return;
        if (Drag.FromSlotIndex is not { } slotIndex) return;

        var lineup = PeriodLineups[periodId];
        var position = GetAllSlots(periodId)[slotIndex];

        var fieldEntry = lineup.FirstOrDefault(p => p.PlayerId == Drag.PlayerId && !p.IsSubstitute);
        var subEntry = lineup.FirstOrDefault(p => p.PlayerId == subPlayerId && p.IsSubstitute);
        if (fieldEntry is null || subEntry is null) return;

        SendToBench(fieldEntry);

        subEntry.IsSubstitute = false;
        subEntry.Position = position;
        subEntry.SlotIndex = slotIndex;

        Drag.Clear();
        StateHasChanged();
    }

    private void OnPlayerRemoved(int periodId, int slotIndex)
    {
        if (HasBeenPlayed(periodId)) return;

        var existing = BuildSlotAssignments(periodId)[slotIndex];
        if (existing is not null)
            PeriodLineups[periodId].Remove(existing);
        StateHasChanged();
    }

    private void RemoveSub(int periodId, GamePlayerPosition sub)
    {
        if (HasBeenPlayed(periodId)) return;

        PeriodLineups[periodId].Remove(sub);
    }

    private static void SendToBench(GamePlayerPosition entry)
    {
        entry.IsSubstitute = true;
        entry.Position = entry.Player?.PreferredPosition ?? entry.Position;
        entry.SlotIndex = null;
    }

    private static GamePlayerPosition CreateEntry(
        Player player, PlayerPosition position, int? slotIndex, bool isSubstitute = false) =>
        new()
        {
            PlayerId = player.Id,
            Player = player,
            Position = position,
            SlotIndex = slotIndex,
            IsSubstitute = isSubstitute
        };

    /// The shape belongs to the game, so it saves on the spot. The service reshapes the line-ups it holds; the same is done here to the
    /// copy on the page, so a drag not yet saved survives the switch instead of being reloaded away.
    private async Task OnFormationChanged(FormationType formation)
    {
        if (GameData is null || formation == GameData.FormationType) return;

        var result = await GameService.SaveFormationAsync(GameId, formation);
        if (!Snackbar.ReportFailure(L, result)) return;

        ReshapeCachedLineups(formation);
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
        && !HasBeenPlayed(GameData.Periods.OrderBy(p => p.PeriodType).ToList()[ActivePeriodIndex + 1].Id);

    private void CopyToNextPeriod()
    {
        if (!CanCopyToNextPeriod) return;

        var orderedPeriods = GameData!.Periods.OrderBy(p => p.PeriodType).ToList();
        var sourcePeriod = orderedPeriods[ActivePeriodIndex];
        var nextPeriod = orderedPeriods[ActivePeriodIndex + 1];

        // Fresh entries with Id = 0 — the copy must not claim the source rows' identities.
        PeriodLineups[nextPeriod.Id] = PeriodLineups[sourcePeriod.Id]
            .Select(pp => new GamePlayerPosition
            {
                PlayerId = pp.PlayerId,
                Player = pp.Player,
                Position = pp.Position,
                SlotIndex = pp.SlotIndex,
                IsSubstitute = pp.IsSubstitute
            })
            .ToList();

        ActivePeriodIndex++;

        Logger.LogInformation("Copied lineup from {SourcePeriod} to {NextPeriod} for game {GameId}",
            sourcePeriod.PeriodType, nextPeriod.PeriodType, GameId);
        Snackbar.Add(L["Lineup copied to {0}", L[nextPeriod.PeriodType.DisplayName()].Value], Severity.Info);
    }

    private async Task SaveAll()
    {
        // Re-read rather than believed from the cached game: this page may have been open since before kick-off, in which case its own
        // copy still has every half down as a plan and the save would be refused one period at a time.
        var currentResult = await GameService.GetByIdAsync(GameId, Cancellation);
        if (!Snackbar.ReportFailure(L, currentResult)) return;

        var played = currentResult.Value!.Periods.Where(p => p.HasKickedOff).OrderBy(p => p.PeriodType).ToList();
        var playedIds = played.Select(p => p.Id).ToHashSet();

        var failures = new List<string>();

        foreach (var (periodId, lineup) in PeriodLineups.Where(entry => !playedIds.Contains(entry.Key)))
        {
            var result = await GameService.SavePeriodLineupAsync(periodId, lineup);
            if (result.IsFailure) failures.Add(UiFeedback.Translate(L, result));
        }

        if (failures.Count > 0)
        {
            Snackbar.Add(L["Save failed: {0}", string.Join("; ", failures)], Severity.Error);
            return;
        }

        if (played.Count > 0)
        {
            Snackbar.Add(
                L["Left as the touchline recorded it: {0}",
                    string.Join(", ", played.Select(p => L[p.PeriodType.DisplayName()].Value))],
                Severity.Info);
        }

        Snackbar.Add(L["All lineups saved!"], Severity.Success);
        Logger.LogInformation("Saved all lineups for game {GameId}", GameId);

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

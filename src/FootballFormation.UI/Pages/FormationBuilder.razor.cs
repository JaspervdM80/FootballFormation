using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.UI.Components;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;

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

    /// <summary>The squad of this game's season. Taken from the game, not from the season picker —
    /// the builder is scoped to one fixture and must not follow a global filter.</summary>
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;
    private Dictionary<int, List<GamePlayerPosition>> PeriodLineups { get; } = [];
    private int ActivePeriodIndex { get; set; }
    private LineupDragState Drag { get; } = new();

    protected override async Task OnInitializedAsync()
    {
        var gameResult = await GameService.GetByIdAsync(GameId, Cancellation);

        // An abandoned load is not a missing game, and must not redirect: the visitor is already
        // on another page by then. See CancellableComponent.
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

        // The full pool is still needed: a player who was lined up but has since left the squad
        // must stay visible in the playing-time table (see GetPlayingTimeData).
        var playersResult = await PlayerService.GetAllAsync(Cancellation);
        AllPlayers = Snackbar.ReportFailure(L, playersResult) ? playersResult.Value! : [];

        CacheLineups();

        Logger.LogDebug("Loaded formation builder for game {GameId} vs {Opponent}",
            GameId, GameData.Opponent);
    }

    // --- Roster ---

    /// <summary>Squad players who are available, plus guests explicitly added to this game.</summary>
    private List<Player> RosterPlayers =>
        AllPlayers is null || GameData is null ? [] : GameData.SelectRoster(AllPlayers, Squad);

    /// <summary>Squad players who opted out of this game. Guests are simply not added, not unavailable.</summary>
    private List<Player> UnavailablePlayers
    {
        get
        {
            if (AllPlayers is null || GameData is null) return [];

            var unavailable = GameData.UnavailablePlayerIds.ToHashSet();
            return AllPlayers.Where(p => Squad.IsFullMember(p.Id) && unavailable.Contains(p.Id)).ToList();
        }
    }

    private List<Player> GetAvailablePlayers(int periodId)
    {
        var usedIds = PeriodLineups.TryGetValue(periodId, out var lineup)
            ? lineup.Select(p => p.PlayerId).ToHashSet()
            : [];

        return RosterPlayers.Where(p => !usedIds.Contains(p.Id)).ToList();
    }

    // --- Slots ---

    private PlayerPosition[] GetAllSlots(int periodId)
    {
        var period = GameData!.Periods.First(p => p.Id == periodId);
        return FormationSlots.For(period.FormationTypeOverride ?? GameData.FormationType);
    }

    /// <summary>Who is standing where in this period, using the same rule the pitch draws with.</summary>
    private GamePlayerPosition?[] BuildSlotAssignments(int periodId) =>
        FormationSlots.Assign(GetAllSlots(periodId), PeriodLineups.GetValueOrDefault(periodId, []));

    // --- Drag & drop ---

    private void OnPlayerDragStart(int playerId) => Drag.StartFromList(playerId);

    private void OnSubDragStart(int playerId) => Drag.StartFromSub(playerId);

    private void OnPitchPlayerDragStart(int periodId, int slotIndex)
    {
        var existing = BuildSlotAssignments(periodId)[slotIndex];
        if (existing is null) return;

        Drag.StartFromPitch(existing.PlayerId, slotIndex);
    }

    private void OnPlayerDropped(int periodId, int slotIndex)
    {
        if (Drag.PlayerId is null || AllPlayers is null) return;

        var slots = GetAllSlots(periodId);
        var position = slots[slotIndex];
        var lineup = PeriodLineups[periodId];

        if (Drag.FromSlotIndex is { } sourceSlotIndex)
        {
            // Drag from one slot to another — swap
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

            // A drop from the bench sends the current occupant back to the bench;
            // a drop from the list replaces them outright.
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
        if (Drag.PlayerId is null || AllPlayers is null) return;

        if (AllPlayers.FirstOrDefault(p => p.Id == Drag.PlayerId) is { } player)
        {
            var lineup = PeriodLineups[periodId];
            lineup.RemoveAll(p => p.PlayerId == player.Id);
            lineup.Add(CreateEntry(player, player.PreferredPosition, slotIndex: null, isSubstitute: true));
        }

        Drag.Clear();
        StateHasChanged();
    }

    /// <summary>Drop of a dragged starter onto a bench player: the two trade places.</summary>
    private void OnSwapFieldPlayerWithSub(int periodId, int subPlayerId)
    {
        if (Drag.PlayerId is null || Drag.PlayerId == subPlayerId) return;
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
        var existing = BuildSlotAssignments(periodId)[slotIndex];
        if (existing is not null)
            PeriodLineups[periodId].Remove(existing);
        StateHasChanged();
    }

    private void RemoveSub(int periodId, GamePlayerPosition sub) => PeriodLineups[periodId].Remove(sub);

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

    // --- Periods ---

    private bool IsLastPeriodSelected =>
        GameData is not null && ActivePeriodIndex >= GameData.Periods.Count - 1;

    private void CopyToNextPeriod()
    {
        if (GameData is null || IsLastPeriodSelected) return;

        var orderedPeriods = GameData.Periods.OrderBy(p => p.PeriodType).ToList();
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

    // --- Persistence ---

    private async Task SaveAll()
    {
        var failures = new List<string>();

        foreach (var (periodId, lineup) in PeriodLineups)
        {
            var result = await GameService.SavePeriodLineupAsync(periodId, lineup);
            if (result.IsFailure) failures.Add(result.Error!);
        }

        if (failures.Count > 0)
        {
            Snackbar.Add(L["Save failed: {0}", string.Join("; ", failures)], Severity.Error);
            return;
        }

        Snackbar.Add(L["All lineups saved!"], Severity.Success);
        Logger.LogInformation("Saved all lineups for game {GameId}", GameId);

        // Reload so the cached entries carry the DB-generated IDs
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

    // --- Playing time overview ---

    private List<PlayingTimeRow> GetPlayingTimeData()
    {
        if (GameData is null || AllPlayers is null) return [];

        // Roster plus anyone already placed in a lineup (e.g. a guest removed from the
        // game after being lined up), so the table always accounts for the whole pitch.
        var linedUpIds = PeriodLineups.Values
            .SelectMany(lineup => lineup)
            .Select(p => p.PlayerId)
            .ToHashSet();
        var players = AllPlayers.Where(p => GameData.IsInRoster(p, Squad) || linedUpIds.Contains(p.Id));

        return PlayingTimeReport.Build(GameData, players, PeriodLineups);
    }

    /// <summary>The playing-time table colours its cells with the same five tiers as the pitch.</summary>
    private static string GetFitCssClass(PositionFit fit) => Pitch.FitCssClass(fit);

    private static Color GetTimeColor(double percentage) => percentage switch
    {
        >= 90 => Color.Success,
        >= 50 => Color.Info,
        >= 25 => Color.Warning,
        _ => Color.Error
    };
}

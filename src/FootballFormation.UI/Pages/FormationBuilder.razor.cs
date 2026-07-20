using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class FormationBuilder
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ILogger<FormationBuilder> Logger { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private List<Player>? AllPlayers { get; set; }
    private Dictionary<int, List<GamePlayerPosition>> PeriodLineups { get; } = [];
    private int ActivePeriodIndex { get; set; }
    private LineupDragState Drag { get; } = new();

    protected override async Task OnInitializedAsync()
    {
        var gameResult = await GameService.GetByIdAsync(GameId);
        if (!Snackbar.ReportFailure(gameResult))
        {
            Logger.LogWarning("Game {GameId} not found, redirecting to games list", GameId);
            Navigation.NavigateTo("/games");
            return;
        }

        GameData = gameResult.Value!;

        var playersResult = await PlayerService.GetAllAsync();
        AllPlayers = Snackbar.ReportFailure(playersResult) ? playersResult.Value! : [];

        CacheLineups();

        Logger.LogDebug("Loaded formation builder for game {GameId} vs {Opponent}",
            GameId, GameData.Opponent);
    }

    private void NavigateBack() => Navigation.NavigateTo("/games");

    // --- Roster ---

    /// <summary>Squad players who are available, plus guests explicitly added to this game.</summary>
    private List<Player> RosterPlayers =>
        AllPlayers is null || GameData is null ? [] : GameData.SelectRoster(AllPlayers);

    /// <summary>Squad players who opted out of this game. Guests are simply not added, not unavailable.</summary>
    private List<Player> UnavailablePlayers
    {
        get
        {
            if (AllPlayers is null || GameData is null) return [];

            var unavailable = GameData.UnavailablePlayerIds.ToHashSet();
            return AllPlayers.Where(p => !p.IsGuest && unavailable.Contains(p.Id)).ToList();
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
        var formation = period.FormationTypeOverride ?? GameData.FormationType;
        return [PlayerPosition.GK, .. formation.DefaultPositions()];
    }

    /// <summary>
    /// Matches lineup entries to formation slots.
    /// Uses SlotIndex as the source of truth; falls back to position matching for legacy data.
    /// </summary>
    private GamePlayerPosition?[] BuildSlotAssignments(int periodId)
    {
        var slots = GetAllSlots(periodId);
        var lineup = PeriodLineups.GetValueOrDefault(periodId, []);
        var assignments = new GamePlayerPosition?[slots.Length];
        var starters = lineup.Where(p => !p.IsSubstitute).ToList();

        // Pass 1: direct slot index assignment
        foreach (var entry in starters.Where(p => p.SlotIndex is not null).ToList())
        {
            var idx = entry.SlotIndex!.Value;
            if (idx >= 0 && idx < slots.Length && assignments[idx] is null)
            {
                assignments[idx] = entry;
                starters.Remove(entry);
            }
        }

        // Pass 2: position-based fallback for legacy data without SlotIndex
        for (int i = 0; i < slots.Length; i++)
        {
            if (assignments[i] is not null) continue;
            var match = starters.FirstOrDefault(p => p.Position == slots[i]);
            if (match is not null)
            {
                assignments[i] = match;
                starters.Remove(match);
            }
        }
        return assignments;
    }

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
        var gameResult = await GameService.GetByIdAsync(GameId);
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
        var players = AllPlayers.Where(p => GameData.IsInRoster(p) || linedUpIds.Contains(p.Id));

        return PlayingTimeReport.Build(GameData, players, PeriodLineups);
    }

    private static string GetFitCssClass(PositionFit fit) => fit switch
    {
        PositionFit.Preferred => "fit-preferred",
        PositionFit.NaturalFit => "fit-natural",
        PositionFit.Alternative => "fit-alternative",
        PositionFit.Compatible => "fit-compatible",
        _ => "fit-out-of-position"
    };

    private static Color GetTimeColor(double percentage) => percentage switch
    {
        >= 90 => Color.Success,
        >= 50 => Color.Info,
        >= 25 => Color.Warning,
        _ => Color.Error
    };
}

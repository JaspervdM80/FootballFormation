using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
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

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private List<Player>? AllPlayers { get; set; }
    private Dictionary<int, List<GamePlayerPosition>> PeriodLineups { get; set; } = new();
    private int ActivePeriodIndex { get; set; }
    private int? DraggedPlayerId { get; set; }
    private int? DraggedFromSlotIndex { get; set; }
    private bool DraggedFromSub { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var gameResult = await GameService.GetByIdAsync(GameId);
        if (gameResult.IsFailure)
        {
            Logger.LogWarning("Game {GameId} not found, redirecting to games list", GameId);
            Snackbar.Add(gameResult.Error!, Severity.Error);
            Navigation.NavigateTo("/games");
            return;
        }

        GameData = gameResult.Value!;

        var playersResult = await PlayerService.GetAllAsync();
        if (playersResult.IsFailure)
        {
            Snackbar.Add(playersResult.Error!, Severity.Error);
            AllPlayers = [];
            return;
        }

        AllPlayers = playersResult.Value!;

        foreach (var period in GameData.Periods)
        {
            PeriodLineups[period.Id] = period.PlayerPositions.ToList();
        }

        Logger.LogDebug("Loaded formation builder for game {GameId} vs {Opponent}",
            GameId, GameData.Opponent);
    }

    private HashSet<int> UnavailableIds => GameData?.UnavailablePlayerIds.ToHashSet() ?? [];

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

    private List<Player> GetAvailablePlayers(int periodId)
    {
        if (AllPlayers is null) return [];
        var usedIds = PeriodLineups.TryGetValue(periodId, out var lineup)
            ? lineup.Select(p => p.PlayerId).ToHashSet()
            : [];
        var unavailable = UnavailableIds;
        return AllPlayers.Where(p => !usedIds.Contains(p.Id) && !unavailable.Contains(p.Id)).ToList();
    }

    private void OnPlayerDragStart(int playerId)
    {
        DraggedPlayerId = playerId;
        DraggedFromSlotIndex = null;
        DraggedFromSub = false;
    }

    private void OnPitchPlayerDragStart(int periodId, int slotIndex)
    {
        var assignments = BuildSlotAssignments(periodId);
        var existing = assignments[slotIndex];
        if (existing is null) return;

        DraggedPlayerId = existing.PlayerId;
        DraggedFromSlotIndex = slotIndex;
        DraggedFromSub = false;
    }

    private void OnSubDragStart(int playerId)
    {
        DraggedPlayerId = playerId;
        DraggedFromSlotIndex = null;
        DraggedFromSub = true;
    }

    private void OnPlayerDropped(int periodId, int slotIndex)
    {
        if (DraggedPlayerId is null || AllPlayers is null) return;

        var slots = GetAllSlots(periodId);
        var position = slots[slotIndex];
        var lineup = PeriodLineups[periodId];

        if (DraggedFromSlotIndex is not null)
        {
            // Drag from one slot to another — swap
            var sourceSlotIndex = DraggedFromSlotIndex.Value;
            var sourcePosition = slots[sourceSlotIndex];
            var assignments = BuildSlotAssignments(periodId);

            var source = assignments[sourceSlotIndex];
            var target = assignments[slotIndex];

            if (source is not null)
            {
                source.Position = position;
                source.SlotIndex = slotIndex;
                if (target is not null)
                {
                    target.Position = sourcePosition;
                    target.SlotIndex = sourceSlotIndex;
                }
            }

            DraggedPlayerId = null;
            DraggedFromSlotIndex = null;
            DraggedFromSub = false;
            StateHasChanged();
            return;
        }

        var player = AllPlayers.FirstOrDefault(p => p.Id == DraggedPlayerId);
        if (player is null) return;

        var wasFromSub = DraggedFromSub;

        // Remove player from any current assignment
        lineup.RemoveAll(p => p.PlayerId == player.Id);

        // Handle existing player at this specific slot
        var slotAssignments = BuildSlotAssignments(periodId);
        var existingAtSlot = slotAssignments[slotIndex];
        if (existingAtSlot is not null)
        {
            if (wasFromSub)
            {
                existingAtSlot.IsSubstitute = true;
                existingAtSlot.Position = existingAtSlot.Player?.PreferredPosition ?? existingAtSlot.Position;
                existingAtSlot.SlotIndex = null;
            }
            else
            {
                lineup.Remove(existingAtSlot);
            }
        }

        lineup.Add(new GamePlayerPosition
        {
            PlayerId = player.Id,
            Player = player,
            Position = position,
            SlotIndex = slotIndex,
            IsSubstitute = false
        });

        DraggedPlayerId = null;
        DraggedFromSlotIndex = null;
        DraggedFromSub = false;
        StateHasChanged();
    }

    private void OnPlayerDroppedToSub(int periodId)
    {
        if (DraggedPlayerId is null || AllPlayers is null) return;

        var player = AllPlayers.FirstOrDefault(p => p.Id == DraggedPlayerId);
        if (player is null) return;

        var lineup = PeriodLineups[periodId];

        lineup.RemoveAll(p => p.PlayerId == player.Id);

        if (!lineup.Any(p => p.PlayerId == player.Id && p.IsSubstitute))
        {
            lineup.Add(new GamePlayerPosition
            {
                PlayerId = player.Id,
                Player = player,
                Position = player.PreferredPosition,
                IsSubstitute = true
            });
        }

        DraggedPlayerId = null;
        DraggedFromSlotIndex = null;
        DraggedFromSub = false;
        StateHasChanged();
    }

    private void OnPlayerRemoved(int periodId, int slotIndex)
    {
        var assignments = BuildSlotAssignments(periodId);
        var existing = assignments[slotIndex];
        if (existing is not null)
            PeriodLineups[periodId].Remove(existing);
        StateHasChanged();
    }

    private List<Player> GetUnavailablePlayers()
    {
        if (AllPlayers is null || GameData is null) return [];
        var unavailable = UnavailableIds;
        return AllPlayers.Where(p => unavailable.Contains(p.Id)).ToList();
    }

    private void NavigateBack() => Navigation.NavigateTo("/games");

    private void OnSwapFieldPlayerWithSub(int periodId, int subPlayerId)
    {
        if (DraggedPlayerId is null || AllPlayers is null) return;
        if (DraggedPlayerId == subPlayerId) return;
        if (DraggedFromSlotIndex is null) return;

        var lineup = PeriodLineups[periodId];
        var slots = GetAllSlots(periodId);
        var slotIndex = DraggedFromSlotIndex.Value;
        var position = slots[slotIndex];

        var fieldEntry = lineup.FirstOrDefault(p => p.PlayerId == DraggedPlayerId && !p.IsSubstitute);
        var subEntry = lineup.FirstOrDefault(p => p.PlayerId == subPlayerId && p.IsSubstitute);

        if (fieldEntry is null || subEntry is null) return;

        var subPlayer = subEntry.Player;

        fieldEntry.IsSubstitute = true;
        fieldEntry.Position = fieldEntry.Player?.PreferredPosition ?? fieldEntry.Position;
        fieldEntry.SlotIndex = null;

        subEntry.IsSubstitute = false;
        subEntry.Position = position;
        subEntry.SlotIndex = slotIndex;

        DraggedPlayerId = null;
        DraggedFromSlotIndex = null;
        DraggedFromSub = false;
        StateHasChanged();
    }

    private void RemoveSub(int periodId, GamePlayerPosition sub)
    {
        PeriodLineups[periodId].Remove(sub);
    }

    private async Task SaveAll()
    {
        var failedPeriods = new List<string>();

        foreach (var (periodId, lineup) in PeriodLineups)
        {
            var result = await GameService.SavePeriodLineupAsync(periodId, lineup);
            if (result.IsFailure)
            {
                failedPeriods.Add(result.Error!);
            }
        }

        if (failedPeriods.Count > 0)
        {
            Snackbar.Add($"Save failed: {string.Join("; ", failedPeriods)}", Severity.Error);
            return;
        }

        Snackbar.Add("All lineups saved!", Severity.Success);
        Logger.LogInformation("Saved all lineups for game {GameId}", GameId);

        // Reload to get fresh data with DB-generated IDs
        var gameResult = await GameService.GetByIdAsync(GameId);
        if (gameResult.IsSuccess && gameResult.Value is not null)
        {
            GameData = gameResult.Value;
            foreach (var period in GameData.Periods)
            {
                PeriodLineups[period.Id] = period.PlayerPositions.ToList();
            }
        }
    }

    private bool IsLastPeriodSelected =>
        GameData is not null &&
        ActivePeriodIndex >= GameData.Periods.Count - 1;

    private void CopyToNextPeriod()
    {
        if (GameData is null || IsLastPeriodSelected) return;

        var orderedPeriods = GameData.Periods.OrderBy(p => p.PeriodType).ToList();
        var sourcePeriod = orderedPeriods[ActivePeriodIndex];
        var nextPeriod = orderedPeriods[ActivePeriodIndex + 1];
        var sourceLineup = PeriodLineups[sourcePeriod.Id];

        PeriodLineups[nextPeriod.Id] = sourceLineup.Select(pp => new GamePlayerPosition
        {
            PlayerId = pp.PlayerId,
            Player = pp.Player,
            Position = pp.Position,
            SlotIndex = pp.SlotIndex,
            IsSubstitute = pp.IsSubstitute
        }).ToList();

        ActivePeriodIndex++;

        Logger.LogInformation("Copied lineup from {SourcePeriod} to {NextPeriod} for game {GameId}",
            sourcePeriod.PeriodType, nextPeriod.PeriodType, GameId);
        Snackbar.Add($"Lineup copied to {nextPeriod.PeriodType.DisplayName()}", Severity.Info);
    }

    // --- Playing Time Overview ---

    private int PeriodCount => GameData?.SplitType == GameSplitType.Halves ? 2 : 4;
    private int PeriodDurationMinutes => (GameData?.GameDurationMinutes ?? 60) / PeriodCount;
    private string PeriodLabel => GameData?.SplitType == GameSplitType.Halves ? "half" : "quarter";

    private bool IsAllPeriodsFilledOut()
    {
        if (GameData is null) return false;

        foreach (var period in GameData.Periods)
        {
            if (!PeriodLineups.TryGetValue(period.Id, out var lineup)) return false;

            var formation = period.FormationTypeOverride ?? GameData.FormationType;
            var requiredSlots = formation.DefaultPositions().Length;
            var starters = lineup.Count(p => !p.IsSubstitute);
            if (starters < requiredSlots) return false;
        }
        return true;
    }

    private List<PlayingTimeRow> GetPlayingTimeData()
    {
        if (GameData is null || AllPlayers is null) return [];

        var orderedPeriods = GameData.Periods.OrderBy(p => p.PeriodType).ToList();
        var unavailable = UnavailableIds;

        var rows = new List<PlayingTimeRow>();
        var gameDuration = GameData.GameDurationMinutes;

        foreach (var player in AllPlayers.Where(p => !unavailable.Contains(p.Id)))
        {
            var row = new PlayingTimeRow
            {
                PlayerId = player.Id,
                Player = player,
                PlayerName = player.DisplayName,
                ShirtNumber = player.ShirtNumber,
                PeriodDetails = new Dictionary<int, PeriodDetail>()
            };

            var periodsPlaying = 0;

            foreach (var period in orderedPeriods)
            {
                var lineup = PeriodLineups.GetValueOrDefault(period.Id, []);
                var entry = lineup.FirstOrDefault(p => p.PlayerId == player.Id);

                if (entry is null)
                {
                    row.PeriodDetails[period.Id] = new PeriodDetail
                    {
                        Status = PeriodPlayStatus.NotPlaying
                    };
                }
                else if (entry.IsSubstitute)
                {
                    row.PeriodDetails[period.Id] = new PeriodDetail
                    {
                        Status = PeriodPlayStatus.Substitute,
                        Position = entry.Position,
                        Fit = PositionFitHelper.GetFit(player, entry.Position)
                    };
                }
                else
                {
                    row.PeriodDetails[period.Id] = new PeriodDetail
                    {
                        Status = PeriodPlayStatus.Starting,
                        Position = entry.Position,
                        Fit = PositionFitHelper.GetFit(player, entry.Position)
                    };
                    periodsPlaying++;
                }
            }

            row.TotalMinutes = periodsPlaying * PeriodDurationMinutes;
            row.Percentage = gameDuration > 0
                ? Math.Round((double)row.TotalMinutes / gameDuration * 100, 0)
                : 0;

            rows.Add(row);
        }

        return rows.OrderByDescending(r => r.TotalMinutes)
                    .ThenBy(r => r.ShirtNumber ?? 99)
                    .ThenBy(r => r.PlayerName)
                    .ToList();
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

public class PlayingTimeRow
{
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public string PlayerName { get; set; } = "";
    public int? ShirtNumber { get; set; }
    public Dictionary<int, PeriodDetail> PeriodDetails { get; set; } = new();
    public int TotalMinutes { get; set; }
    public double Percentage { get; set; }
}

public class PeriodDetail
{
    public PeriodPlayStatus Status { get; set; }
    public PlayerPosition? Position { get; set; }
    public PositionFit? Fit { get; set; }
}

public enum PeriodPlayStatus
{
    NotPlaying,
    Starting,
    Substitute
}

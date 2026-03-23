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
    private PlayerPosition? DraggedFromPosition { get; set; }

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
        DraggedFromPosition = null;
    }

    private void OnPitchPlayerDragStart(int periodId, PlayerPosition position)
    {
        var lineup = PeriodLineups[periodId];
        var existing = lineup.FirstOrDefault(p => p.Position == position && !p.IsSubstitute);
        if (existing is null) return;

        DraggedPlayerId = existing.PlayerId;
        DraggedFromPosition = position;
    }

    private void OnPlayerDropped(int periodId, PlayerPosition position)
    {
        if (DraggedPlayerId is null || AllPlayers is null) return;

        var lineup = PeriodLineups[periodId];

        if (DraggedFromPosition is not null)
        {
            var sourcePos = DraggedFromPosition.Value;
            var target = lineup.FirstOrDefault(p => p.Position == position && !p.IsSubstitute);
            var source = lineup.FirstOrDefault(p => p.Position == sourcePos && !p.IsSubstitute);

            if (source is not null)
            {
                source.Position = position;
                if (target is not null)
                    target.Position = sourcePos;
            }

            DraggedPlayerId = null;
            DraggedFromPosition = null;
            StateHasChanged();
            return;
        }

        var player = AllPlayers.FirstOrDefault(p => p.Id == DraggedPlayerId);
        if (player is null) return;

        lineup.RemoveAll(p => p.PlayerId == player.Id);
        lineup.RemoveAll(p => p.Position == position && !p.IsSubstitute);

        lineup.Add(new GamePlayerPosition
        {
            PlayerId = player.Id,
            Player = player,
            Position = position,
            IsSubstitute = false
        });

        DraggedPlayerId = null;
        DraggedFromPosition = null;
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
        DraggedFromPosition = null;
        StateHasChanged();
    }

    private void OnPlayerRemoved(int periodId, PlayerPosition position)
    {
        PeriodLineups[periodId].RemoveAll(p => p.Position == position && !p.IsSubstitute);
        StateHasChanged();
    }

    private List<Player> GetUnavailablePlayers()
    {
        if (AllPlayers is null || GameData is null) return [];
        var unavailable = UnavailableIds;
        return AllPlayers.Where(p => unavailable.Contains(p.Id)).ToList();
    }

    private void NavigateBack() => Navigation.NavigateTo("/games");

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

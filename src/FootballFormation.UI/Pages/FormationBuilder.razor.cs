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

    // --- Drag & drop ---

    private void OnPlayerDragStart(int playerId) => Drag.StartFromList(playerId);

    private void OnPitchPlayerDragStart(int periodId, PlayerPosition position)
    {
        var existing = FindStarter(PeriodLineups[periodId], position);
        if (existing is null) return;

        Drag.StartFromPitch(existing.PlayerId, position);
    }

    private void OnPlayerDropped(int periodId, PlayerPosition position)
    {
        if (Drag.PlayerId is null || AllPlayers is null) return;

        var lineup = PeriodLineups[periodId];

        if (Drag.FromPosition is { } sourcePosition)
        {
            SwapStarters(lineup, sourcePosition, position);
        }
        else if (AllPlayers.FirstOrDefault(p => p.Id == Drag.PlayerId) is { } player)
        {
            lineup.RemoveAll(p => p.PlayerId == player.Id);
            lineup.RemoveAll(p => p.Position == position && !p.IsSubstitute);
            lineup.Add(CreateEntry(player, position, isSubstitute: false));
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
            lineup.Add(CreateEntry(player, player.PreferredPosition, isSubstitute: true));
        }

        Drag.Clear();
        StateHasChanged();
    }

    private void OnPlayerRemoved(int periodId, PlayerPosition position)
    {
        PeriodLineups[periodId].RemoveAll(p => p.Position == position && !p.IsSubstitute);
        StateHasChanged();
    }

    private void RemoveSub(int periodId, GamePlayerPosition sub) => PeriodLineups[periodId].Remove(sub);

    /// <summary>Moves the dragged starter to the target slot, sending any occupant back the other way.</summary>
    private static void SwapStarters(List<GamePlayerPosition> lineup, PlayerPosition from, PlayerPosition to)
    {
        var source = FindStarter(lineup, from);
        if (source is null) return;

        var target = FindStarter(lineup, to);

        source.Position = to;
        if (target is not null) target.Position = from;
    }

    private static GamePlayerPosition? FindStarter(List<GamePlayerPosition> lineup, PlayerPosition position) =>
        lineup.FirstOrDefault(p => p.Position == position && !p.IsSubstitute);

    private static GamePlayerPosition CreateEntry(Player player, PlayerPosition position, bool isSubstitute) =>
        new()
        {
            PlayerId = player.Id,
            Player = player,
            Position = position,
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
                IsSubstitute = pp.IsSubstitute
            })
            .ToList();

        ActivePeriodIndex++;

        Logger.LogInformation("Copied lineup from {SourcePeriod} to {NextPeriod} for game {GameId}",
            sourcePeriod.PeriodType, nextPeriod.PeriodType, GameId);
        Snackbar.Add($"Lineup copied to {nextPeriod.PeriodType.DisplayName()}", Severity.Info);
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
            Snackbar.Add($"Save failed: {string.Join("; ", failures)}", Severity.Error);
            return;
        }

        Snackbar.Add("All lineups saved!", Severity.Success);
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

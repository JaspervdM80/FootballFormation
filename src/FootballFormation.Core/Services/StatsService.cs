using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

// Paired because guest status is per season, so the figures are only correct against these squads.
public sealed record SeasonStatsView(SeasonStats Stats, SeasonSquads Squads);

// One entry covers all three statistics pages: SeasonStatsReport builds its per-player figures by
// calling PlayerStatsReport unchanged. See docs/patterns/service-structure.md.
public class StatsService(
    GameService games,
    SeasonSquadService squads,
    StatsCache cache,
    ILogger<StatsService> logger)
{
    // seasonId null covers every season.
    public Task<Result<SeasonStatsView>> GetSeasonAsync(
        int? seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the statistics", cancellationToken, async () =>
        {
            // Before the load, never after — see StatsCache.KeyFor.
            var key = cache.KeyFor($"season:{seasonId}");

            if (cache.TryGet<SeasonStatsView>(key, out var hit)) return Result.Success(hit);

            var inputs = await LoadAsync(seasonId, cancellationToken);
            if (inputs.IsFailure) return inputs.To<SeasonStatsView>();

            var (allGames, allSquads) = inputs.Value!;

            // The squad, not everyone on file, or a past season shows today's squad.
            var view = new SeasonStatsView(
                SeasonStatsReport.Build(allSquads.AllPlayers, allGames, allSquads),
                allSquads);

            cache.Set(key, view);

            logger.LogDebug("Built statistics for season {SeasonId} over {Count} games",
                seasonId, allGames.Count);
            return Result.Success(view);
        });

    public Task<Result<PlayerStats>> GetPlayerAsync(
        Player player, int? seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the player statistics", cancellationToken, async () =>
        {
            var seasonResult = await GetSeasonAsync(seasonId, cancellationToken);
            if (seasonResult.IsFailure) return seasonResult.To<PlayerStats>();

            var existing = seasonResult.Value!.Stats.Players.FirstOrDefault(p => p.Player.Id == player.Id);
            if (existing is not null) return Result.Success(existing);

            // In no squad of this season, but the page is reachable for anyone on file.
            var key = cache.KeyFor($"player:{player.Id}:{seasonId}");

            if (cache.TryGet<PlayerStats>(key, out var hit)) return Result.Success(hit);

            var inputs = await LoadAsync(seasonId, cancellationToken);
            if (inputs.IsFailure) return inputs.To<PlayerStats>();

            var (allGames, allSquads) = inputs.Value!;
            var stats = PlayerStatsReport.Build(player, allGames, allSquads);

            cache.Set(key, stats);
            return Result.Success(stats);
        });

    private async Task<Result<(List<Game> Games, SeasonSquads Squads)>> LoadAsync(
        int? seasonId, CancellationToken cancellationToken)
    {
        var squadsResult = await squads.GetSquadsAsync(seasonId, cancellationToken);
        if (squadsResult.IsFailure) return squadsResult.To<(List<Game>, SeasonSquads)>();

        var gamesResult = await games.GetAllWithDetailsAsync(seasonId, cancellationToken);
        if (gamesResult.IsFailure) return gamesResult.To<(List<Game>, SeasonSquads)>();

        return Result.Success((gamesResult.Value!, squadsResult.Value!));
    }
}

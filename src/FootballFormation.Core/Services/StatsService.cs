using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>A season's figures and the squads they were built from — the pages filter by squad
/// membership, and guest status is per season.</summary>
public sealed record SeasonStatsView(SeasonStats Stats, SeasonSquads Squads);

/// <summary>
/// The statistics pages' one way in: loads what a report needs, builds it, and serves it from
/// <see cref="StatsCache"/> until something is written.
/// <para>
/// One entry covers all three pages, because <see cref="SeasonStatsReport.Build"/> makes its
/// per-player figures by calling <see cref="PlayerStatsReport.Build"/> unchanged — so a player's
/// entry in <see cref="SeasonStats.Players"/> is the object <c>/players/{id}/stats</c> would have
/// built for itself. See docs/patterns/service-structure.md for why this caches the report rather
/// than the markup or the loaded games.
/// </para>
/// </summary>
public class StatsService(
    GameService games,
    SeasonSquadService squads,
    StatsCache cache,
    ILogger<StatsService> logger)
{
    /// <param name="seasonId">The season to report on. Null covers every season.</param>
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

            // The squad is the roster, not everyone on file: that is what stops a past season
            // showing today's squad.
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

            // In no squad of this season, but the page is reachable for anyone on file. Rare enough
            // to earn its own load rather than a wider season report.
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

using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>A season's figures and the squads they were built from. Paired because every page that
/// shows the figures also filters them by squad membership, and the two are only correct
/// together — the squads are per season, and so is who was a guest.</summary>
public sealed record SeasonStatsView(SeasonStats Stats, SeasonSquads Squads);

/// <summary>
/// The statistics pages' one way in: loads what a report needs, builds it, and serves it from
/// <see cref="StatsCache"/> until something is written.
/// <para>
/// It composes two other services, which the rest of Core deliberately avoids — but the
/// alternative was the same four lines of loading copied into three pages, which is where they
/// were, and a cache in front of a page cannot skip the load the page has already started. The
/// split is by use case, not by layer: this is "what the statistics pages need", and it computes
/// rather than delegates.
/// </para>
/// <para>
/// **One entry serves all three pages.** <see cref="SeasonStatsReport.Build"/> produces its
/// per-player figures by calling <see cref="PlayerStatsReport.Build"/> unchanged, so a player's
/// entry in <see cref="SeasonStats.Players"/> is the identical object <c>/players/{id}/stats</c>
/// would have built for itself. Caching the season report therefore caches the player reports too,
/// and a squad of twenty costs one entry rather than twenty-one.
/// </para>
/// <para>
/// Nothing here is admin-gated: statistics are public. What the cache holds is public too — the
/// reports carry the minutes for everyone and the *pages* hide them from a visitor
/// (<c>_isAdmin</c>), so one cached copy is correct for an admin and a visitor alike and there is
/// no per-viewer keying to get wrong. That is the reason this caches the report and not the
/// rendered page: markup varies by who is reading it, and by their language and season cookie.
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
            // Before the load, never after: see StatsCache.KeyFor. A write that lands while the
            // report below is being built must orphan it, not overwrite the entry with it.
            var key = cache.KeyFor($"season:{seasonId}");

            if (cache.TryGet<SeasonStatsView>(key, out var hit))
            {
                logger.LogDebug("Served statistics for season {SeasonId} from the cache", seasonId);
                return Result.Success(hit);
            }

            var inputs = await LoadAsync(seasonId, cancellationToken);
            if (inputs.IsFailure) return inputs.To<SeasonStatsView>();

            var (allGames, allSquads) = inputs.Value!;

            // The squad is the authoritative roster, so the player list comes from it rather than
            // from everyone on file. That is what stops a past season showing today's squad.
            var view = new SeasonStatsView(
                SeasonStatsReport.Build(allSquads.AllPlayers, allGames, allSquads),
                allSquads);

            cache.Set(key, view);

            logger.LogDebug("Built statistics for season {SeasonId} over {Count} games",
                seasonId, allGames.Count);
            return Result.Success(view);
        });

    /// <summary>
    /// One player's figures for the season, which is all but always a lookup in the season report
    /// that is already cached.
    /// </summary>
    public Task<Result<PlayerStats>> GetPlayerAsync(
        Player player, int? seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the player statistics", cancellationToken, async () =>
        {
            var seasonResult = await GetSeasonAsync(seasonId, cancellationToken);
            if (seasonResult.IsFailure) return seasonResult.To<PlayerStats>();

            var existing = seasonResult.Value!.Stats.Players.FirstOrDefault(p => p.Player.Id == player.Id);
            if (existing is not null) return Result.Success(existing);

            // Nobody in this season's squad, but the page is still reachable for anyone on file —
            // a player who left, or one reached from another season. Rare enough to earn its own
            // load rather than a wider season report, and cached after the first visit.
            var key = cache.KeyFor($"player:{player.Id}:{seasonId}");

            if (cache.TryGet<PlayerStats>(key, out var hit)) return Result.Success(hit);

            var inputs = await LoadAsync(seasonId, cancellationToken);
            if (inputs.IsFailure) return inputs.To<PlayerStats>();

            var (allGames, allSquads) = inputs.Value!;
            var stats = PlayerStatsReport.Build(player, allGames, allSquads);

            cache.Set(key, stats);

            logger.LogDebug("Built statistics for player {PlayerId} outside the squad of season {SeasonId}",
                player.Id, seasonId);
            return Result.Success(stats);
        });

    /// <summary>
    /// Everything a report is built from. Both are needed together and neither is useful alone:
    /// guest status is per season and lives on the squads, and a game only knows its season id.
    /// </summary>
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

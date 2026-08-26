namespace FootballFormation.Core.Services;

/// Reads only, and public like every other read. Writing to a live match is split by what happens on the touchline instead:
/// <see cref="MatchClockService"/>, <see cref="MatchGoalService"/>, <see cref="MatchSubstitutionService"/>.
public class LiveMatchService(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider time,
    ILogger<LiveMatchService> logger)
{
    /// Everything the live screen renders, in one round trip.
    public Task<Result<Game>> GetLiveAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load live match", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // No tracking, and it matters: a tracked Game would keep handing back the score, clock and state from its first load while
            // newly inserted goals appeared alongside them. Identity resolution keeps the shared Player rows as single instances.
            var game = await db.Games
                .AsNoTrackingWithIdentityResolution()
                .WithNamedLineups()
                .WithGoalsAndScorers()
                .WithSubstitutionPlayers()
                .WithInjuries()
                .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

            if (game is null)
            {
                logger.LogWarning("Live match {GameId} not found", gameId);
                return LiveMatchQueries.GameNotFound<Game>(gameId);
            }

            return Result.Success(game);
        });

    /// Whatever is being played now, else today's fixture — from midnight until the final score is in, so match day is signposted all
    /// day rather than only between kick-off and the last whistle.
    public Task<Result<Game?>> GetTodaysMatchAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "find today's match", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // A match in progress wins whatever the calendar says — it can have kicked off before midnight, and it is the one someone
            // standing at a pitch is watching.
            var game = (await db.Games
                .AsNoTracking()
                .Where(g => g.MatchState == MatchState.InProgress)
                .ToListAsync(cancellationToken))
                .NewestFirst()
                .FirstOrDefault();

            if (game is not null) return Result.Success<Game?>(game);

            // The whole calendar day, kick-off time or not. A double-header shows the one still to be played before the one already done.
            var today = time.GetLocalNow().Date;
            var tomorrow = today.AddDays(1);

            // The one date comparison left in SQL, so the home page does not read the games table whole on every hit. See QueryTags.
            game = (await db.Games
                .AsNoTracking()
                .TagWith(QueryTags.ComparesDatesInSql)
                .Where(g => g.Date >= today && g.Date < tomorrow)
                .ToListAsync(cancellationToken))
                .OrderBy(g => g.MatchState == MatchState.Finished)
                .ThenBy(g => g.Date)
                .ThenBy(g => g.Id)
                .FirstOrDefault();

            return Result.Success(game);
        });
}

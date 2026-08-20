using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Reading a match that is being played: everything the live screen renders, and which match — if
/// any — the home page should be pointing at today.
/// <para>
/// Both reads are public, like every other read in the app. Writing to a live match is split by
/// what is happening on the touchline instead: <see cref="MatchClockService"/> for the clock and
/// the run of play, <see cref="MatchGoalService"/> for goals, <see cref="MatchSubstitutionService"/>
/// for substitutions.
/// </para>
/// </summary>
public class LiveMatchService(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider time,
    ILogger<LiveMatchService> logger)
{
    /// <summary>
    /// Everything the live screen renders, in one round trip: the periods with their lineups and
    /// players, the goals, the substitutions with both players named, and the injuries.
    /// </summary>
    public Task<Result<Game>> GetLiveAsync(int gameId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load live match", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // No tracking, and it matters: a spectator's circuit keeps one scoped DbContext for
            // its whole life, so a tracked Game would keep returning the score, clock and state
            // from its first load while newly inserted goals appeared alongside them. Identity
            // resolution keeps the shared Player rows as single instances.
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

    /// <summary>
    /// The match the home page should point at, or null on an ordinary day. That is whatever is
    /// being played right now, and otherwise today's fixture — from the moment the day starts,
    /// through the match, until the final score is in — so match day is signposted all day rather
    /// than only between kick-off and the last whistle.
    /// </summary>
    public Task<Result<Game?>> GetTodaysMatchAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "find today's match", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // A match in progress wins whatever the calendar says: it can have been kicked off
            // before midnight, and it is the one someone standing at a pitch is watching. Nothing
            // stops two being in progress at once, so the most recent by date wins.
            var game = (await db.Games
                .AsNoTracking()
                .Where(g => g.MatchState == MatchState.InProgress)
                .ToListAsync(cancellationToken))
                .NewestFirst()
                .FirstOrDefault();

            if (game is not null) return Result.Success<Game?>(game);

            // "Today" is the whole calendar day, kick-off time or not — a fixture is signposted
            // from midnight whatever time it actually starts. A double-header shows the one still
            // to be played before the one already done.
            var today = time.GetLocalNow().Date;
            var tomorrow = today.AddDays(1);

            // The one date comparison left in SQL, so this does not read every game ever played on
            // each home-page hit. The tag is what says so out loud — see QueryTags.
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

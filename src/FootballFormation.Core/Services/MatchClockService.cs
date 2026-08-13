using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// The clock and the run of play: kick-off, half time and the final whistle.
/// <para>
/// A match is two halves, whether its line-ups were planned in halves or in quarters. A line-up
/// planned for the middle of a half never reaches this service — the coach works through it by
/// hand while the clock runs — so the only stoppage here is half time.
/// </para>
/// <para>
/// There is no pause: the clock runs from kick-off until the half is whistled off. A youth match
/// is not paused at the touchline, and a clock that could be stopped by a stray tap is a clock the
/// season's minutes cannot be trusted from.
/// </para>
/// <para>
/// This is where the arithmetic a season's statistics are built on lives — the banked seconds and
/// the started/ended marks on each half are what <c>GameMinutesReport</c> later credits players
/// with — so it is the piece of the live match kept on its own and driven to exact instants under
/// test.
/// </para>
/// </summary>
public class MatchClockService(
    IDbContextFactory<AppDbContext> dbFactory,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchClockService> logger)
{
    /// <summary>
    /// The clock every match-time decision reads. Injected rather than taken straight from
    /// <see cref="DateTime.UtcNow"/> so the half arithmetic can be driven to an exact instant
    /// under test — it is the part of the live match most likely to be silently wrong, and a
    /// season's statistics depend on it.
    /// </summary>
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public Task<Result<Game>> StartMatchAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "start match",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.NotStarted)
                return Result.Failure<Game>("This match has already been started");

            var first = game.Periods.OrderBy(p => p.PeriodType).FirstOrDefault();
            if (first is null) return Result.Failure<Game>("This game has no line-up to play");

            game.MatchState = MatchState.InProgress;
            game.ClockAccumulatedSeconds = 0;
            game.ClockRunningSince = UtcNow;
            game.LivePeriodId = first.Id;
            first.StartedAtSeconds = 0;
            first.EndedAtSeconds = null;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Started live match {GameId} in the {Half} with line-up {PeriodId}",
                gameId, first.PeriodType.Half(), first.Id);
            return Result.Success(game);
        });

    /// <summary>Whistles the half off. The clock stops and no half is live until the next kicks off.</summary>
    public Task<Result<Game>> EndHalfAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "end the half",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            var current = game.LiveHalf();
            if (current is null) return Result.Failure<Game>("No half is being played");

            BankClock(game);
            current.EndedAtSeconds = game.ClockAccumulatedSeconds;
            game.LivePeriodId = null;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Ended the {Half} of game {GameId} at {Seconds}s",
                current.PeriodType.Half(), gameId, current.EndedAtSeconds);
            return Result.Success(game);
        });

    /// <summary>
    /// Kicks off the half after the break. <see cref="Game.NextHalf"/> decides which line-up opens
    /// it, skipping any planned for the middle of the half just played.
    /// </summary>
    public Task<Result<Game>> StartNextHalfAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "start the next half",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState != MatchState.InProgress)
                return Result.Failure<Game>("This match is not in progress");
            if (game.LivePeriodId is not null)
                return Result.Failure<Game>("End the current half first");

            var next = game.NextHalf();
            if (next is null)
                return Result.Failure<Game>("Both halves have been played — finish the match instead");

            BankClock(game);
            next.StartedAtSeconds = game.ClockAccumulatedSeconds;
            next.EndedAtSeconds = null;
            game.LivePeriodId = next.Id;
            game.ClockRunningSince = UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Started the {Half} of game {GameId} at {Seconds}s",
                next.PeriodType.Half(), gameId, next.StartedAtSeconds);
            return Result.Success(game);
        });

    public Task<Result<Game>> FinishMatchAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "finish the match",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return NotFound(gameId);

            if (game.MatchState == MatchState.NotStarted)
                return Result.Failure<Game>("This match has not been started");

            BankClock(game);

            var current = game.LiveHalf();
            if (current is not null) current.EndedAtSeconds = game.ClockAccumulatedSeconds;

            game.LivePeriodId = null;
            game.MatchState = MatchState.Finished;

            // Recounted here rather than through MatchGoalService: this is the recount that settles
            // the game, and from here it counts towards the season.
            var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync(cancellationToken);
            game.CountScoreFrom(goals);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Finished game {GameId} at {Home}-{Away} after {Seconds}s",
                gameId, game.ScoreHome, game.ScoreAway, game.ClockAccumulatedSeconds);
            return Result.Success(game);
        });

    /// <summary>
    /// Moves the time run so far out of the anchor and into the banked total, leaving the clock
    /// stopped. Every state change calls this first so no seconds are lost or double-counted.
    /// </summary>
    private void BankClock(Game game)
    {
        game.ClockAccumulatedSeconds = game.ElapsedSecondsAt(UtcNow);
        game.ClockRunningSince = null;
    }

    private Result<Game> NotFound(int gameId)
    {
        logger.LogWarning("Live match {GameId} not found", gameId);
        return LiveMatchQueries.GameNotFound<Game>(gameId);
    }
}

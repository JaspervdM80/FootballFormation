namespace FootballFormation.Core.Services;

/// Half time is the only stoppage: a line-up planned for the middle of a half never reaches this service, and there is deliberately no
/// pause — a clock a stray tap could stop is a clock the season's minutes cannot be trusted from.
public class MatchClockService(
    IDbContextFactory<AppDbContext> dbFactory,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchClockService> logger)
{
    /// Injected rather than <see cref="DateTime.UtcNow"/> so the half arithmetic can be driven to an exact instant under test — it is
    /// the part of the live match most likely to be silently wrong, and a season's statistics depend on it.
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public Task<Result<Game>> StartMatchAsync(int gameId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "start the match",
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

    /// The clock stops and no half is live until the next kicks off.
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

    /// <see cref="Game.NextHalf"/> decides which line-up opens it, skipping any planned for the middle of the half just played.
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

            // Recounted here rather than through MatchGoalService: this is the recount that settles the game for the season.
            var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync(cancellationToken);
            game.CountScoreFrom(goals);

            // The other place a match becomes part of the record.
            await StandingInjuries.RecordAsync(db, game, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Finished game {GameId} at {Home}-{Away} after {Seconds}s",
                gameId, game.ScoreHome, game.ScoreAway, game.ClockAccumulatedSeconds);
            return Result.Success(game);
        });

    /// Every state change calls this first, so no seconds are lost or double-counted between the anchor and the banked total.
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

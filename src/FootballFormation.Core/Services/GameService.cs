namespace FootballFormation.Core.Services;

public class GameService(
    IDbContextFactory<AppDbContext> dbFactory,
    SeasonService seasons,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<GameService> logger)
{
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// A null <paramref name="seasonId"/> loads every season.
    public Task<Result<List<Game>>> GetAllAsync(
        int? seasonId = null, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load games", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var games = (await db.Games
                .AsNoTracking()
                .Where(g => seasonId == null || g.SeasonId == seasonId)
                .WithPeriods()
                .WithGoalsAndScorers()
                .ToListAsync(cancellationToken))
                .NewestFirst();

            logger.LogDebug("Retrieved {Count} games for season {SeasonId}", games.Count, seasonId);
            return Result.Success(games);
        });

    /// A null <paramref name="seasonId"/> loads every season.
    public Task<Result<List<Game>>> GetAllWithDetailsAsync(
        int? seasonId = null, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load game details", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var games = (await db.Games
                .AsNoTracking()
                .Where(g => seasonId == null || g.SeasonId == seasonId)
                .WithPeriodLineups()
                .WithGoals()
                .WithSubstitutions()
                .WithInjuries()
                .ToListAsync(cancellationToken))
                .NewestFirst();

            logger.LogDebug("Retrieved {Count} games with details for season {SeasonId}",
                games.Count, seasonId);
            return Result.Success(games);
        });

    public Task<Result<Game>> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load game", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.Games
                .AsNoTracking()
                .WithNamedLineups()
                .WithGoalsAndScorers()
                .WithSubstitutions()
                .WithInjuries()
                .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

            if (game is null) return GameNotFound.For<Game>(logger, id);

            return Result.Success(game);
        });

    public Task<Result<Game>> CreateAsync(Game game, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create game", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Resolved here rather than at the call site, so "every game has a season" is an invariant no caller can bypass.
            var placement = await seasons.PlaceAsync(db, game.SeasonId, game.Date, cancellationToken);
            if (placement.IsFailure) return placement.To<Game>();
            (game.SeasonId, game.TeamId) = placement.Value;

            foreach (var periodType in PeriodTypeExtensions.ForSplitType(game.SplitType))
            {
                game.Periods.Add(new GamePeriod { PeriodType = periodType });
            }

            db.Games.Add(game);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created game vs {Opponent} on {Date} in season {SeasonId} (ID: {GameId})",
                game.Opponent, game.Date.ToString("yyyy-MM-dd"), game.SeasonId, game.Id);
            return Result.Success(game);
        });

    public Task<Result> UpdateAsync(Game game, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update game", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var stored = await db.Games.WithPeriodLineups().FirstOrDefaultAsync(g => g.Id == game.Id, cancellationToken);
            if (stored is null) return GameNotFound.For(logger, game.Id);

            // The periods are the split's, so a new split needs new periods. A match under way has recorded events against its own.
            if (game.SplitType != stored.SplitType)
            {
                if (stored.MatchState != MatchState.NotStarted || stored.Periods.Any(p => p.HasKickedOff))
                {
                    logger.LogWarning("Cannot change the split of game {GameId}: it has kicked off", game.Id);
                    return Result.Failure("A match that has kicked off keeps the split it was played in");
                }

                var periods = stored.PeriodsFor(game.SplitType);
                db.GamePeriods.RemoveRange(stored.Periods.ToList());
                db.GamePeriods.AddRange(periods);
            }

            if (game.SeasonId != stored.SeasonId)
            {
                var placement = await seasons.PlaceAsync(db, game.SeasonId, game.Date, cancellationToken);
                if (placement.IsFailure) return placement;
                stored.SeasonId = placement.Value.SeasonId;
            }

            stored.TakeFixtureDetailsFrom(game);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated game vs {Opponent} in season {SeasonId} (ID: {GameId})",
                game.Opponent, game.SeasonId, game.Id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "delete the game", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // FirstOrDefault, not Find: Find bypasses the query filter, so it would fetch another team's game by id.
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
            if (game is null) return GameNotFound.For(logger, id);

            db.Games.Remove(game);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted game vs {Opponent} (ID: {GameId})", game.Opponent, game.Id);
            return Result.Success();
        });

    public Task<Result> SaveScoreAsync(
        int gameId, int? scoreHome, int? scoreAway, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "save the score", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
            if (game is null) return GameNotFound.For(logger, gameId);

            game.ScoreHome = scoreHome;
            game.ScoreAway = scoreAway;

            // Asked on every save; RecordAsync answers only the first, which is what stamps who was injured at the time.
            if (game.IsComplete)
                await StandingInjuries.RecordAsync(db, game, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Saved score {Home}-{Away} for game {GameId}",
                scoreHome, scoreAway, gameId);
            return Result.Success();
        });

    /// <param name="recountScoreline">True at the touchline, where the scoreline is the goals and both commit together. False on the
    /// result page, where recounting a hand-typed 3-1 would cut it down to the two goals whose scorer someone remembered.</param>
    public Task<Result<GameGoal>> AddGoalAsync(
        GameGoal goal, bool recountScoreline = false, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "add the goal", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (!await db.GameInScopeAsync(goal.GameId, cancellationToken))
                return GameNotFound.For<GameGoal>(logger, goal.GameId);

            goal.RecordedAt = UtcNow;

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            db.GameGoals.Add(goal);
            await db.SaveChangesAsync(cancellationToken);

            if (recountScoreline)
                await RecountScorelineAsync(db, goal.GameId, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            if (goal.ScorerId is not null)
                await db.Entry(goal).Reference(g => g.Scorer).LoadAsync(cancellationToken);
            if (goal.AssisterId is not null)
                await db.Entry(goal).Reference(g => g.Assister).LoadAsync(cancellationToken);

            logger.LogInformation("Added goal by player {ScorerId} for game {GameId}",
                goal.ScorerId, goal.GameId);
            return Result.Success(goal);
        });

    /// <inheritdoc cref="AddGoalAsync(GameGoal, bool, CancellationToken)" path="/param[@name='recountScoreline']"/>
    public Task<Result> RemoveGoalAsync(
        int goalId, bool recountScoreline = false, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "remove goal", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var goal = await db.FindInScopeAsync<GameGoal>(goalId, cancellationToken);
            if (goal is null)
            {
                logger.LogWarning("Cannot remove goal {GoalId}: not found", goalId);
                return Result.Failure("Goal not found");
            }

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            db.GameGoals.Remove(goal);
            await db.SaveChangesAsync(cancellationToken);

            if (recountScoreline)
                await RecountScorelineAsync(db, goal.GameId, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            logger.LogInformation("Removed goal {GoalId}", goalId);
            return Result.Success();
        });

    /// Must run on the caller's context and inside its transaction — one cannot span two — and only after its save, or two touchline
    /// devices logging a goal at once would both read-modify-write the same scoreline. See docs/patterns/transactions-and-writes.md.
    internal static async Task RecountScorelineAsync(
        AppDbContext db, int gameId, CancellationToken cancellationToken)
    {
        var game = await db.Games.FindAsync([gameId], cancellationToken);
        if (game is null) return;

        game.CountScoreFrom(await db.GameGoals
            .Where(g => g.GameId == gameId)
            .ToListAsync(cancellationToken));

        await db.SaveChangesAsync(cancellationToken);
    }
}

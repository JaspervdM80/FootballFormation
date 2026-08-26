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
                .WithGoals()
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
                .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

            if (game is null)
            {
                logger.LogWarning("Game {GameId} not found", id);
                return Result.Failure<Game>("Game with ID {0} not found", id);
            }

            return Result.Success(game);
        });

    public Task<Result<Game>> CreateAsync(Game game, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create game", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Resolved here rather than at the call site, so "every game has a season" is an invariant no caller can bypass. SeasonId 0
            // is the dialog's "auto by date" default.
            if (game.SeasonId == 0)
            {
                var seasonResult = await seasons.GetOrCreateForDateAsync(game.Date, cancellationToken);
                if (seasonResult.IsFailure) return seasonResult.To<Game>();

                game.SeasonId = seasonResult.Value!.Id;
            }

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

            // Never DbSet.Update here: it walks the whole loaded graph and marks every row Modified, so renaming an opponent would
            // rewrite the match's entire lineup history. Setting State attaches the root alone.
            db.Entry(game).State = EntityState.Modified;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated game vs {Opponent} in season {SeasonId} (ID: {GameId})",
                game.Opponent, game.SeasonId, game.Id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "delete game", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.Games.FindAsync([id], cancellationToken);
            if (game is null)
            {
                logger.LogWarning("Cannot delete game {GameId}: not found", id);
                return Result.Failure("Game not found");
            }

            db.Games.Remove(game);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted game vs {Opponent} (ID: {GameId})", game.Opponent, game.Id);
            return Result.Success();
        });

    public Task<Result> SaveScoreAsync(
        int gameId, int? scoreHome, int? scoreAway, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "save score", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var game = await db.Games.FindAsync([gameId], cancellationToken);
            if (game is null)
            {
                logger.LogWarning("Cannot save score for game {GameId}: not found", gameId);
                return Result.Failure("Game not found");
            }

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
        ServiceOperation.RunAdminAsync(currentUser, logger, "add goal", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // The service's clock, not the entity initializer's wall clock, or a live match driven by a fake clock would still record
            // real timestamps. The initializer stays as the default for a goal built outside a service.
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

            var goal = await db.GameGoals.FindAsync([goalId], cancellationToken);
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
    private static async Task RecountScorelineAsync(
        AppDbContext db, int gameId, CancellationToken cancellationToken)
    {
        var game = await db.Games.FindAsync([gameId], cancellationToken);
        if (game is null) return;

        game.CountScoreFrom(await db.GameGoals
            .Where(g => g.GameId == gameId)
            .ToListAsync(cancellationToken));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <paramref name="includePrivate"/> is a request, not a permission — it is confirmed against <see cref="ICurrentUser"/> below.
    /// The filter belongs in the query because the result page prerenders, so markup that merely hides the row would still ship the text.
    public Task<Result<List<GameComment>>> GetCommentsAsync(
        int gameId, bool includePrivate, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load comments", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            includePrivate = includePrivate && await currentUser.IsAdminAsync();

            // The tie-break runs the other way to a fixture list's: two comments written in the same instant are a feed, newest on top.
            var comments = (await db.GameComments
                .Where(c => c.GameId == gameId && (includePrivate || c.IsPublic))
                .Include(c => c.Author)
                .ToListAsync(cancellationToken))
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .ToList();

            logger.LogDebug("Retrieved {Count} comments for game {GameId} (private included: {IncludePrivate})",
                comments.Count, gameId, includePrivate);
            return Result.Success(comments);
        });

    public Task<Result<GameComment>> AddCommentAsync(
        GameComment comment, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "add comment", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // The service's clock, not the entity initializer's — see AddGoalAsync.
            comment.CreatedAt = UtcNow;

            db.GameComments.Add(comment);
            await db.SaveChangesAsync(cancellationToken);

            // The list shows the author's name, and Add returns the row without it.
            if (comment.AuthorId is not null)
                await db.Entry(comment).Reference(c => c.Author).LoadAsync(cancellationToken);

            logger.LogInformation("Added {Visibility} comment to game {GameId} by user {AuthorId} (ID: {CommentId})",
                comment.IsPublic ? "public" : "private", comment.GameId, comment.AuthorId, comment.Id);
            return Result.Success(comment);
        });

    public Task<Result> UpdateCommentAsync(
        int commentId, string body, bool isPublic, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update comment", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var comment = await db.GameComments.FindAsync([commentId], cancellationToken);
            if (comment is null)
            {
                logger.LogWarning("Cannot update comment {CommentId}: not found", commentId);
                return Result.Failure("Comment not found");
            }

            // Publishing on its own is not an edit — the text is unchanged, so the "edited" marker would be a lie.
            if (comment.Body != body) comment.EditedAt = UtcNow;

            comment.Body = body;
            comment.IsPublic = isPublic;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated comment {CommentId} on game {GameId} to {Visibility}",
                commentId, comment.GameId, isPublic ? "public" : "private");
            return Result.Success();
        });

    public Task<Result> RemoveCommentAsync(int commentId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "remove comment", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var comment = await db.GameComments.FindAsync([commentId], cancellationToken);
            if (comment is null)
            {
                logger.LogWarning("Cannot remove comment {CommentId}: not found", commentId);
                return Result.Failure("Comment not found");
            }

            db.GameComments.Remove(comment);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Removed comment {CommentId} from game {GameId}", commentId, comment.GameId);
            return Result.Success();
        });

    public Task<Result> SavePeriodLineupAsync(
        int periodId, List<GamePlayerPosition> positions, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "save lineup", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Delete-then-insert needs both halves or neither, or a failed insert leaves the period with no line-up at all.
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var existing = await db.GamePlayerPositions
                .Where(pp => pp.GamePeriodId == periodId)
                .ToListAsync(cancellationToken);

            db.GamePlayerPositions.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);

            // Fresh entities with Id = 0 — reusing tracked IDs trips the UNIQUE constraint.
            foreach (var pos in positions)
            {
                db.GamePlayerPositions.Add(new GamePlayerPosition
                {
                    GamePeriodId = periodId,
                    PlayerId = pos.PlayerId,
                    Position = pos.Position,
                    SlotIndex = pos.IsSubstitute ? null : pos.SlotIndex,
                    IsSubstitute = pos.IsSubstitute
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            logger.LogInformation("Saved lineup for period {PeriodId}: {Count} positions",
                periodId, positions.Count);
            return Result.Success();
        });
}

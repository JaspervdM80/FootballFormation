using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class GameService(
    IDbContextFactory<AppDbContext> dbFactory,
    SeasonService seasons,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<GameService> logger)
{
    /// <summary>
    /// The clock for anything this service stamps. Injected rather than read from
    /// <see cref="DateTime.UtcNow"/> for the same reason <see cref="LiveMatchService"/> does it:
    /// a timestamp a test cannot control is a timestamp a test cannot assert on.
    /// </summary>
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// <param name="seasonId">Limits the result to one season. Null loads every season.</param>
    public Task<Result<List<Game>>> GetAllAsync(int? seasonId = null) =>
        ServiceOperation.RunAsync(logger, "load games", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var games = await db.Games
                .AsNoTracking()
                .Where(g => seasonId == null || g.SeasonId == seasonId)
                .Include(g => g.Periods)
                .Include(g => g.Goals)
                .OrderByDescending(g => g.Date)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} games for season {SeasonId}", games.Count, seasonId);
            return Result.Success(games);
        });

    /// <param name="seasonId">Limits the result to one season. Null loads every season.</param>
    public Task<Result<List<Game>>> GetAllWithDetailsAsync(int? seasonId = null) =>
        ServiceOperation.RunAsync(logger, "load game details", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var games = await db.Games
                .AsNoTracking()
                .Where(g => seasonId == null || g.SeasonId == seasonId)
                .Include(g => g.Periods)
                    .ThenInclude(p => p.PlayerPositions)
                .Include(g => g.Goals)
                // Statistics reconstruct playing time from the substitutions (see
                // GameMinutesReport); without them a live-tracked game reads as if the final
                // lineup had been on the pitch from kick-off.
                .Include(g => g.Substitutions)
                .OrderByDescending(g => g.Date)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} games with details for season {SeasonId}",
                games.Count, seasonId);
            return Result.Success(games);
        });

    public Task<Result<Game>> GetByIdAsync(int id) =>
        ServiceOperation.RunAsync(logger, "load game", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // Goals are included twice because EF needs a fresh Include to hang a second
            // ThenInclude off the same navigation. Both must be spelled identically — a filtered
            // and an unfiltered include of one collection is ambiguous. Callers that care about
            // order sort for themselves (MatchResult.razor).
            var game = await db.Games
                .AsNoTracking()
                .Include(g => g.Periods.OrderBy(p => p.PeriodType))
                    .ThenInclude(p => p.PlayerPositions)
                        .ThenInclude(pp => pp.Player)
                .Include(g => g.Goals)
                    .ThenInclude(gl => gl.Scorer)
                .Include(g => g.Goals)
                    .ThenInclude(gl => gl.Assister)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (game is null)
            {
                logger.LogWarning("Game {GameId} not found", id);
                return Result.Failure<Game>("Game with ID {0} not found", id);
            }

            return Result.Success(game);
        });

    public Task<Result<Game>> CreateAsync(Game game) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create game", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // SeasonId 0 is the dialog's "auto by date" default; an explicit choice passes through.
            // Resolving it here rather than at the call site keeps "every game has a season" an
            // invariant no caller can bypass.
            if (game.SeasonId == 0)
            {
                var seasonResult = await seasons.GetOrCreateForDateAsync(game.Date);
                if (seasonResult.IsFailure) return seasonResult.To<Game>();

                game.SeasonId = seasonResult.Value!.Id;
            }

            foreach (var periodType in PeriodTypeExtensions.ForSplitType(game.SplitType))
            {
                game.Periods.Add(new GamePeriod { PeriodType = periodType });
            }

            db.Games.Add(game);
            await db.SaveChangesAsync();

            logger.LogInformation("Created game vs {Opponent} on {Date} in season {SeasonId} (ID: {GameId})",
                game.Opponent, game.Date.ToString("yyyy-MM-dd"), game.SeasonId, game.Id);
            return Result.Success(game);
        });

    public Task<Result> UpdateAsync(Game game) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update game", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // Scalars only. The game handed in came from GetAllWithDetailsAsync, so its Periods,
            // PlayerPositions, Goals and Substitutions are all populated — and DbSet.Update walks
            // that whole graph and marks every row Modified. Renaming an opponent would rewrite
            // the entire lineup history of the match. Setting State on the entry attaches the
            // root alone and leaves the navigations untouched.
            db.Entry(game).State = EntityState.Modified;
            await db.SaveChangesAsync();

            logger.LogInformation("Updated game vs {Opponent} in season {SeasonId} (ID: {GameId})",
                game.Opponent, game.SeasonId, game.Id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "delete game", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var game = await db.Games.FindAsync(id);
            if (game is null)
            {
                logger.LogWarning("Cannot delete game {GameId}: not found", id);
                return Result.Failure("Game not found");
            }

            db.Games.Remove(game);
            await db.SaveChangesAsync();

            logger.LogInformation("Deleted game vs {Opponent} (ID: {GameId})", game.Opponent, game.Id);
            return Result.Success();
        });

    public Task<Result> SaveScoreAsync(int gameId, int? scoreHome, int? scoreAway) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "save score", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var game = await db.Games.FindAsync(gameId);
            if (game is null)
            {
                logger.LogWarning("Cannot save score for game {GameId}: not found", gameId);
                return Result.Failure("Game not found");
            }

            game.ScoreHome = scoreHome;
            game.ScoreAway = scoreAway;
            await db.SaveChangesAsync();

            logger.LogInformation("Saved score {Home}-{Away} for game {GameId}",
                scoreHome, scoreAway, gameId);
            return Result.Success();
        });

    public Task<Result<GameGoal>> AddGoalAsync(GameGoal goal) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "add goal", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // Stamped here rather than left to the property initializer on the entity. That
            // initializer reads the wall clock at construction, which meant a live match driven by
            // a fake clock still recorded real timestamps — the one thing TimeProvider exists to
            // prevent. The initializer stays as a sensible default for a goal built outside a
            // service; when a service saves one, the service's clock decides.
            goal.RecordedAt = UtcNow;

            db.GameGoals.Add(goal);
            await db.SaveChangesAsync();

            // Reload with navigation properties
            if (goal.ScorerId is not null)
                await db.Entry(goal).Reference(g => g.Scorer).LoadAsync();
            if (goal.AssisterId is not null)
                await db.Entry(goal).Reference(g => g.Assister).LoadAsync();

            logger.LogInformation("Added goal by player {ScorerId} for game {GameId}",
                goal.ScorerId, goal.GameId);
            return Result.Success(goal);
        });

    public Task<Result> RemoveGoalAsync(int goalId) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "remove goal", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var goal = await db.GameGoals.FindAsync(goalId);
            if (goal is null)
            {
                logger.LogWarning("Cannot remove goal {GoalId}: not found", goalId);
                return Result.Failure("Goal not found");
            }

            db.GameGoals.Remove(goal);
            await db.SaveChangesAsync();

            logger.LogInformation("Removed goal {GoalId}", goalId);
            return Result.Success();
        });

    /// <param name="includePrivate">
    /// True only for an admin. The filter lives in the query rather than in the page so a private
    /// body never reaches a visitor at all — the result page prerenders server-side, so markup that
    /// merely hides the row would still ship the text.
    /// <para>
    /// Asking is not the same as being allowed: the flag is confirmed against
    /// <see cref="ICurrentUser"/> below, so a caller that passes true without being an admin gets
    /// the public comments and nothing else. This is the one read with something to hide, and it
    /// should not be the one place a boolean argument is taken on trust.
    /// </para>
    /// </param>
    public Task<Result<List<GameComment>>> GetCommentsAsync(int gameId, bool includePrivate) =>
        ServiceOperation.RunAsync(logger, "load comments", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            includePrivate = includePrivate && await currentUser.IsAdminAsync();

            var comments = await db.GameComments
                .Where(c => c.GameId == gameId && (includePrivate || c.IsPublic))
                .Include(c => c.Author)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} comments for game {GameId} (private included: {IncludePrivate})",
                comments.Count, gameId, includePrivate);
            return Result.Success(comments);
        });

    public Task<Result<GameComment>> AddCommentAsync(GameComment comment) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "add comment", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // The service's clock, not the entity initializer's — see AddGoalAsync.
            comment.CreatedAt = UtcNow;

            db.GameComments.Add(comment);
            await db.SaveChangesAsync();

            // The list shows the author's name, and Add returns the row without it.
            if (comment.AuthorId is not null)
                await db.Entry(comment).Reference(c => c.Author).LoadAsync();

            logger.LogInformation("Added {Visibility} comment to game {GameId} by user {AuthorId} (ID: {CommentId})",
                comment.IsPublic ? "public" : "private", comment.GameId, comment.AuthorId, comment.Id);
            return Result.Success(comment);
        });

    public Task<Result> UpdateCommentAsync(int commentId, string body, bool isPublic) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update comment", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var comment = await db.GameComments.FindAsync(commentId);
            if (comment is null)
            {
                logger.LogWarning("Cannot update comment {CommentId}: not found", commentId);
                return Result.Failure("Comment not found");
            }

            // Publishing on its own is not an edit — the text is unchanged, so the "edited" marker
            // would be a lie.
            if (comment.Body != body) comment.EditedAt = UtcNow;

            comment.Body = body;
            comment.IsPublic = isPublic;
            await db.SaveChangesAsync();

            logger.LogInformation("Updated comment {CommentId} on game {GameId} to {Visibility}",
                commentId, comment.GameId, isPublic ? "public" : "private");
            return Result.Success();
        });

    public Task<Result> RemoveCommentAsync(int commentId) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "remove comment", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var comment = await db.GameComments.FindAsync(commentId);
            if (comment is null)
            {
                logger.LogWarning("Cannot remove comment {CommentId}: not found", commentId);
                return Result.Failure("Comment not found");
            }

            db.GameComments.Remove(comment);
            await db.SaveChangesAsync();

            logger.LogInformation("Removed comment {CommentId} from game {GameId}", commentId, comment.GameId);
            return Result.Success();
        });

    public Task<Result> SavePeriodLineupAsync(int periodId, List<GamePlayerPosition> positions) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "save lineup", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // Delete-then-insert needs both halves or neither: without the transaction, a failure
            // on the insert leaves the period with no lineup at all rather than the one it had.
            await using var tx = await db.Database.BeginTransactionAsync();

            var existing = await db.GamePlayerPositions
                .Where(pp => pp.GamePeriodId == periodId)
                .ToListAsync();

            db.GamePlayerPositions.RemoveRange(existing);
            await db.SaveChangesAsync();

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

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            logger.LogInformation("Saved lineup for period {PeriodId}: {Count} positions",
                periodId, positions.Count);
            return Result.Success();
        });
}

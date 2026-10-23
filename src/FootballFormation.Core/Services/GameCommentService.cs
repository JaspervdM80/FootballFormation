namespace FootballFormation.Core.Services;

public class GameCommentService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<GameCommentService> logger)
{
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// <paramref name="includePrivate"/> is a request, not a permission — it is confirmed against <see cref="ICurrentUser"/> below.
    /// The filter belongs in the query because the result page prerenders, so markup that merely hides the row would still ship the text.
    public Task<Result<List<GameComment>>> GetCommentsAsync(
        int gameId, bool includePrivate, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load comments", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Comments carry no filter of their own; gate the read on the game being the scope's so another team's feed is never read by id.
            if (!await db.GameInScopeAsync(gameId, cancellationToken))
                return Result.Success(new List<GameComment>());

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
        ServiceOperation.RunAdminAsync(currentUser, logger, "add the comment", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (!await db.GameInScopeAsync(comment.GameId, cancellationToken))
                return GameNotFound.For<GameComment>(logger, comment.GameId);

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

            var comment = await db.FindInScopeAsync<GameComment>(commentId, cancellationToken);
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

            var comment = await db.FindInScopeAsync<GameComment>(commentId, cancellationToken);
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
}

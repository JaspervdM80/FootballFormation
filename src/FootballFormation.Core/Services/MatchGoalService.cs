namespace FootballFormation.Core.Services;

/// Adds the one thing only a match in progress knows: the half being played and the reading on the clock. The write itself is delegated
/// to <see cref="GameService"/>, so the goal and the recounted scoreline go in together rather than across two contexts.
public class MatchGoalService(
    IDbContextFactory<AppDbContext> dbFactory,
    GameService games,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchGoalService> logger)
{
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// A null <paramref name="scorerId"/> means an opponent goal — we do not track their players.
    public Task<Result<GameGoal>> LogGoalAsync(
        int gameId, int? scorerId, int? assisterId, bool isOwnGoal, bool isOpponentGoal,
        CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "log the goal",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Line-ups included: the half being played is half of what places the goal.
            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<GameGoal>(gameId);

            if (scorerId is null && !isOpponentGoal)
                return Result.Failure<GameGoal>("A goal for us needs a scorer");

            var goal = new GameGoal
            {
                GameId = gameId,
                ScorerId = scorerId,
                AssisterId = assisterId,
                // Where it happened, not what a scoreboard made of it — so a half whose timings are corrected later takes its goals with it.
                GamePeriodId = game.CurrentOrLastHalf()?.Id,
                AtSeconds = game.ElapsedSecondsAt(UtcNow),
                IsOwnGoal = isOwnGoal,
                IsOpponentGoal = isOpponentGoal
            };

            return await games.AddGoalAsync(goal, recountScoreline: true, cancellationToken);
        });

    /// Removes a goal and pulls the scoreline back in step with what is left.
    public Task<Result> RemoveGoalAsync(
        int gameId, int goalId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "remove the goal",
            cancellationToken, async () =>
        {
            var removed = await games.RemoveGoalAsync(goalId, recountScoreline: true, cancellationToken);
            if (removed.IsFailure) return removed.To<int>();

            return Result.Success(gameId);
        });
}

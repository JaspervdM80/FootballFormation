using FootballFormation.Core.Reporting;

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

    /// Corrects a goal already on file — who scored it, who assisted, whether it was an own goal, and the minute it reads. Which side it
    /// counts for is fixed: turning ours into theirs is a different goal, removed and logged again.
    public Task<Result> EditGoalAsync(
        int goalId, int? scorerId, int? assisterId, bool isOwnGoal, int minute,
        CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "correct the goal",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var goal = await db.GameGoals.AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == goalId, cancellationToken);
            if (goal is null || !await db.GameInScopeAsync(goal.GameId, cancellationToken))
            {
                logger.LogWarning("Cannot correct goal {GoalId}: not found", goalId);
                return Result.Failure<int>("Goal not found");
            }

            if (scorerId is null && !goal.IsOpponentGoal)
                return Result.Failure<int>("A goal for us needs a scorer");

            if (isOwnGoal && goal.IsOpponentGoal)
                return Result.Failure<int>("An opponent goal cannot be an own goal");

            var game = await db.LoadWithPeriodsAsync(goal.GameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<int>(goal.GameId);

            var (atSeconds, storedMinute) = Placement(game, goal, minute);

            var updated = await games.UpdateGoalAsync(
                goalId,
                new GameService.GoalCorrection(scorerId, assisterId, isOwnGoal, atSeconds, storedMinute),
                cancellationToken);
            if (updated.IsFailure) return updated.To<int>();

            return Result.Success(goal.GameId);
        });

    /// A goal keeps the shape it was recorded in: one logged live stays placed by the clock inside its own half, one typed in on the result
    /// page by its scoreboard minute. A minute left as it was shown keeps the stored reading, because the shown minute drops stoppage time
    /// and converting an untouched 30+3 back would move the goal three minutes earlier.
    private (int? AtSeconds, int? Minute) Placement(Game game, GameGoal goal, int minute)
    {
        if (minute == MatchClockReport.MinuteOf(game, goal)?.Minute) return (goal.AtSeconds, goal.Minute);

        if (goal.GamePeriodId is not { } periodId
            || game.Periods.FirstOrDefault(p => p.Id == periodId) is not { StartedAtSeconds: { } start } half)
        {
            return (null, minute);
        }

        var end = half.EndedAtSeconds ?? game.ElapsedSecondsAt(UtcNow);
        return (Math.Clamp(MatchClockReport.ElapsedForMinute(game, minute), start, Math.Max(start, end)), null);
    }

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

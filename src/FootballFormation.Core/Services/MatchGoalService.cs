using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Goals as they are logged at the touchline. What this adds is the one thing only a match in
/// progress knows: where in the match the ball went in — the half being played and the reading on
/// the clock, the same pair a substitution carries. Storing the goal — and, at
/// the touchline, recounting the scoreline in the same save — is delegated to
/// <see cref="GameService"/>, so there is one implementation of it and the two rows are written
/// together rather than across two contexts.
/// </summary>
public class MatchGoalService(
    IDbContextFactory<AppDbContext> dbFactory,
    GameService games,
    LiveMatchNotifier notifier,
    TimeProvider time,
    ICurrentUser currentUser,
    ILogger<MatchGoalService> logger)
{
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// <param name="scorerId">Null for an opponent goal — we do not track their players.</param>
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
                // Where it happened, not what a scoreboard made of it. The displayed minute is
                // derived from these two, so a half whose timings are corrected later takes its
                // goals with it — exactly as it already does its substitutions.
                GamePeriodId = game.CurrentOrLastHalf()?.Id,
                AtSeconds = game.ElapsedSecondsAt(UtcNow),
                IsOwnGoal = isOwnGoal,
                IsOpponentGoal = isOpponentGoal
            };

            return await games.AddGoalAsync(goal, recountScoreline: true, cancellationToken);
        });

    /// <summary>Removes a goal and pulls the scoreline back in step with what is left.</summary>
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

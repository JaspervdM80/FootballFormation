using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Goals as they are logged at the touchline. What this adds is the one thing only a match in
/// progress knows: the minute the clock showed when the ball went in. Storing the goal — and, at
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
    /// <summary>The match clock — injected, for the reason <see cref="MatchClockService"/> gives.</summary>
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// <param name="scorerId">Null for an opponent goal — we do not track their players.</param>
    public Task<Result<GameGoal>> LogGoalAsync(
        int gameId, int? scorerId, int? assisterId, bool isOwnGoal, bool isOpponentGoal,
        CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, gameId, currentUser, logger, "log the goal",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Periods included: the minute follows the scoreboard clock, which is measured from
            // the half being played rather than from kick-off.
            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<GameGoal>(gameId);

            if (scorerId is null && !isOpponentGoal)
                return Result.Failure<GameGoal>("A goal for us needs a scorer");

            var clock = MatchClockReport.Build(
                game, game.CurrentOrLastPeriod(), game.ElapsedSecondsAt(UtcNow));

            var goal = new GameGoal
            {
                GameId = gameId,
                ScorerId = scorerId,
                AssisterId = assisterId,
                // The minute the clock showed, so an over-running first half does not push every
                // second-half goal out by the overrun. Stoppage time counts on past the cap rather
                // than pinning several goals to the same minute.
                Minute = clock.Minute,
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

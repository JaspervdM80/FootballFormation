using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// Goals as they are logged at the touchline. Storage itself is delegated to
/// <see cref="GameService"/> so there is one implementation of it; what this adds is the two things
/// only a match in progress knows — the minute the clock showed when the ball went in, and the
/// scoreline that follows from the goals now on file.
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

            // Line-ups included: the minute follows the scoreboard clock, which is measured from
            // the half being played rather than from kick-off.
            var game = await db.LoadWithPeriodsAsync(gameId, cancellationToken);
            if (game is null) return LiveMatchQueries.GameNotFound<GameGoal>(gameId);

            if (scorerId is null && !isOpponentGoal)
                return Result.Failure<GameGoal>("A goal for us needs a scorer");

            var clock = MatchClockReport.Build(
                game, game.CurrentOrLastHalf(), game.ElapsedSecondsAt(UtcNow));

            var goal = new GameGoal
            {
                GameId = gameId,
                ScorerId = scorerId,
                AssisterId = assisterId,
                // The minute the clock showed, so an over-running first half does not push every
                // second-half goal out by the overrun. Stoppage time is kept in the second half of
                // the pair — see GameGoal.AdditionalMinute for why it is not folded into the first.
                Minute = clock.Minute.Minute,
                AdditionalMinute = clock.Minute.Additional,
                IsOwnGoal = isOwnGoal,
                IsOpponentGoal = isOpponentGoal
            };

            var added = await games.AddGoalAsync(goal, cancellationToken);
            if (added.IsFailure) return added;

            await SyncScoreAsync(db, gameId, cancellationToken);
            return added;
        });

    /// <summary>Removes a goal and pulls the scoreline back in step with what is left.</summary>
    public Task<Result> RemoveGoalAsync(
        int gameId, int goalId, CancellationToken cancellationToken = default) =>
        LiveMatchOperation.RunAdminAsync(notifier, currentUser, logger, "remove the goal",
            cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var removed = await games.RemoveGoalAsync(goalId, cancellationToken);
            if (removed.IsFailure) return removed.To<int>();

            await SyncScoreAsync(db, gameId, cancellationToken);
            return Result.Success(gameId);
        });

    /// <summary>Rewrites the scoreline from the logged goals, so the live score is never guessed at.</summary>
    private static async Task SyncScoreAsync(AppDbContext db, int gameId, CancellationToken cancellationToken)
    {
        var game = await db.Games.FindAsync([gameId], cancellationToken);
        if (game is null) return;

        var goals = await db.GameGoals.Where(g => g.GameId == gameId).ToListAsync(cancellationToken);
        game.ScoreHome = Game.CountOurGoals(goals);
        game.ScoreAway = Game.CountTheirGoals(goals);
        await db.SaveChangesAsync(cancellationToken);
    }
}

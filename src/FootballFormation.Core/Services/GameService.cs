using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class GameService(AppDbContext db, SeasonService seasons, ILogger<GameService> logger)
{
    /// <param name="seasonId">Limits the result to one season. Null loads every season.</param>
    public Task<Result<List<Game>>> GetAllAsync(int? seasonId = null) =>
        ServiceOperation.RunAsync(logger, "load games", async () =>
        {
            var games = await db.Games
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
            var games = await db.Games
                .Where(g => seasonId == null || g.SeasonId == seasonId)
                .Include(g => g.Periods)
                    .ThenInclude(p => p.PlayerPositions)
                .Include(g => g.Goals)
                .OrderByDescending(g => g.Date)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} games with details for season {SeasonId}",
                games.Count, seasonId);
            return Result.Success(games);
        });

    public Task<Result<Game>> GetByIdAsync(int id) =>
        ServiceOperation.RunAsync(logger, "load game", async () =>
        {
            var game = await db.Games
                .Include(g => g.Periods.OrderBy(p => p.PeriodType))
                    .ThenInclude(p => p.PlayerPositions)
                        .ThenInclude(pp => pp.Player)
                .Include(g => g.Goals.OrderBy(gl => gl.Minute))
                    .ThenInclude(gl => gl.Scorer)
                .Include(g => g.Goals)
                    .ThenInclude(gl => gl.Assister)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (game is null)
            {
                logger.LogWarning("Game {GameId} not found", id);
                return Result.Failure<Game>($"Game with ID {id} not found");
            }

            return Result.Success(game);
        });

    public Task<Result<Game>> CreateAsync(Game game) =>
        ServiceOperation.RunAsync(logger, "create game", async () =>
        {
            // SeasonId 0 is the dialog's "auto by date" default; an explicit choice passes through.
            // Resolving it here rather than at the call site keeps "every game has a season" an
            // invariant no caller can bypass.
            if (game.SeasonId == 0)
            {
                var seasonResult = await seasons.GetOrCreateForDateAsync(game.Date);
                if (seasonResult.IsFailure) return Result.Failure<Game>(seasonResult.Error!);

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
        ServiceOperation.RunAsync(logger, "update game", async () =>
        {
            db.Games.Update(game);
            await db.SaveChangesAsync();

            logger.LogInformation("Updated game vs {Opponent} in season {SeasonId} (ID: {GameId})",
                game.Opponent, game.SeasonId, game.Id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id) =>
        ServiceOperation.RunAsync(logger, "delete game", async () =>
        {
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
        ServiceOperation.RunAsync(logger, "save score", async () =>
        {
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
        ServiceOperation.RunAsync(logger, "add goal", async () =>
        {
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
        ServiceOperation.RunAsync(logger, "remove goal", async () =>
        {
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

    public Task<Result> SavePeriodLineupAsync(int periodId, List<GamePlayerPosition> positions) =>
        ServiceOperation.RunAsync(logger, "save lineup", async () =>
        {
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

            logger.LogInformation("Saved lineup for period {PeriodId}: {Count} positions",
                periodId, positions.Count);
            return Result.Success();
        });
}

using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class GameService(AppDbContext db, ILogger<GameService> logger)
{
    public async Task<Result<List<Game>>> GetAllAsync()
    {
        try
        {
            var games = await db.Games
                .Include(g => g.Periods)
                .Include(g => g.Goals)
                .OrderByDescending(g => g.Date)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} games", games.Count);
            return Result.Success(games);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve games");
            return Result.Failure<List<Game>>("Failed to load games");
        }
    }

    public async Task<Result<Game>> GetByIdAsync(int id)
    {
        try
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve game {GameId}", id);
            return Result.Failure<Game>("Failed to load game");
        }
    }

    public async Task<Result<Game>> CreateAsync(Game game)
    {
        try
        {
            var periodTypes = PeriodTypeExtensions.ForSplitType(game.SplitType);
            foreach (var periodType in periodTypes)
            {
                game.Periods.Add(new GamePeriod { PeriodType = periodType });
            }

            db.Games.Add(game);
            await db.SaveChangesAsync();

            logger.LogInformation("Created game vs {Opponent} on {Date} (ID: {GameId})",
                game.Opponent, game.Date.ToString("yyyy-MM-dd"), game.Id);
            return Result.Success(game);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create game vs {Opponent}", game.Opponent);
            return Result.Failure<Game>("Failed to create game");
        }
    }

    public async Task<Result> UpdateAsync(Game game)
    {
        try
        {
            db.Games.Update(game);
            await db.SaveChangesAsync();

            logger.LogInformation("Updated game vs {Opponent} (ID: {GameId})", game.Opponent, game.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update game {GameId}", game.Id);
            return Result.Failure("Failed to update game");
        }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete game {GameId}", id);
            return Result.Failure("Failed to delete game");
        }
    }

    public async Task<Result> SaveScoreAsync(int gameId, int? scoreHome, int? scoreAway)
    {
        try
        {
            var game = await db.Games.FindAsync(gameId);
            if (game is null)
                return Result.Failure("Game not found");

            game.ScoreHome = scoreHome;
            game.ScoreAway = scoreAway;
            await db.SaveChangesAsync();

            logger.LogInformation("Saved score {Home}-{Away} for game {GameId}",
                scoreHome, scoreAway, gameId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save score for game {GameId}", gameId);
            return Result.Failure("Failed to save score");
        }
    }

    public async Task<Result<GameGoal>> AddGoalAsync(GameGoal goal)
    {
        try
        {
            db.GameGoals.Add(goal);
            await db.SaveChangesAsync();

            // Reload with navigation properties
            await db.Entry(goal).Reference(g => g.Scorer).LoadAsync();
            if (goal.AssisterId is not null)
                await db.Entry(goal).Reference(g => g.Assister).LoadAsync();

            logger.LogInformation("Added goal by player {ScorerId} for game {GameId}",
                goal.ScorerId, goal.GameId);
            return Result.Success(goal);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add goal for game {GameId}", goal.GameId);
            return Result.Failure<GameGoal>("Failed to add goal");
        }
    }

    public async Task<Result> RemoveGoalAsync(int goalId)
    {
        try
        {
            var goal = await db.GameGoals.FindAsync(goalId);
            if (goal is null)
                return Result.Failure("Goal not found");

            db.GameGoals.Remove(goal);
            await db.SaveChangesAsync();

            logger.LogInformation("Removed goal {GoalId}", goalId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove goal {GoalId}", goalId);
            return Result.Failure("Failed to remove goal");
        }
    }

    public async Task<Result> SavePeriodLineupAsync(int periodId, List<GamePlayerPosition> positions)
    {
        try
        {
            var existing = await db.GamePlayerPositions
                .Where(pp => pp.GamePeriodId == periodId)
                .ToListAsync();

            db.GamePlayerPositions.RemoveRange(existing);
            await db.SaveChangesAsync();

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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save lineup for period {PeriodId}", periodId);
            return Result.Failure("Failed to save lineup");
        }
    }
}

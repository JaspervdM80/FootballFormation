using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class PlayerService(AppDbContext db, ILogger<PlayerService> logger)
{
    public async Task<Result<List<Player>>> GetAllAsync()
    {
        try
        {
            var players = await db.Players
                .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
                .ThenBy(p => p.FirstName)
                .ThenBy(p => p.Surname)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} players", players.Count);
            return Result.Success(players);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve players");
            return Result.Failure<List<Player>>("Failed to load players");
        }
    }

    public async Task<Result<Player>> GetByIdAsync(int id)
    {
        try
        {
            var player = await db.Players.FindAsync(id);
            if (player is null)
            {
                logger.LogWarning("Player {PlayerId} not found", id);
                return Result.Failure<Player>($"Player with ID {id} not found");
            }

            return Result.Success(player);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve player {PlayerId}", id);
            return Result.Failure<Player>("Failed to load player");
        }
    }

    public async Task<Result<Player>> CreateAsync(Player player)
    {
        try
        {
            db.Players.Add(player);
            await db.SaveChangesAsync();

            logger.LogInformation("Created player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
            return Result.Success(player);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create player {PlayerName}", player.DisplayName);
            return Result.Failure<Player>("Failed to create player");
        }
    }

    public async Task<Result> UpdateAsync(Player player)
    {
        try
        {
            db.Players.Update(player);
            await db.SaveChangesAsync();

            logger.LogInformation("Updated player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update player {PlayerId}", player.Id);
            return Result.Failure("Failed to update player");
        }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try
        {
            var player = await db.Players.FindAsync(id);
            if (player is null)
            {
                logger.LogWarning("Cannot delete player {PlayerId}: not found", id);
                return Result.Failure("Player not found");
            }

            db.Players.Remove(player);
            await db.SaveChangesAsync();

            logger.LogInformation("Deleted player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete player {PlayerId}", id);
            return Result.Failure("Failed to delete player");
        }
    }
}

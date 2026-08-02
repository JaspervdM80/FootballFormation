using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class PlayerService(IDbContextFactory<AppDbContext> dbFactory, ILogger<PlayerService> logger)
{
    /// <summary>Everyone on file, in shirt order. Guest status is per season now, so the
    /// guests-last ordering moved to <see cref="SeasonSquad"/>, which is the only thing that can
    /// know it.</summary>
    public Task<Result<List<Player>>> GetAllAsync() =>
        ServiceOperation.RunAsync(logger, "load players", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var players = await db.Players
                .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
                .ThenBy(p => p.FirstName)
                .ThenBy(p => p.Surname)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} players", players.Count);
            return Result.Success(players);
        });

    public Task<Result<Player>> GetByIdAsync(int id) =>
        ServiceOperation.RunAsync(logger, "load player", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var player = await db.Players.FindAsync(id);
            if (player is null)
            {
                logger.LogWarning("Player {PlayerId} not found", id);
                return Result.Failure<Player>("Player with ID {0} not found", id);
            }

            return Result.Success(player);
        });

    public Task<Result<Player>> CreateAsync(Player player) =>
        ServiceOperation.RunAsync(logger, "create player", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            db.Players.Add(player);
            await db.SaveChangesAsync();

            logger.LogInformation("Created player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
            return Result.Success(player);
        });

    public Task<Result> UpdateAsync(Player player) =>
        ServiceOperation.RunAsync(logger, "update player", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            db.Players.Update(player);
            await db.SaveChangesAsync();

            logger.LogInformation("Updated player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id) =>
        ServiceOperation.RunAsync(logger, "delete player", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

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
        });
}

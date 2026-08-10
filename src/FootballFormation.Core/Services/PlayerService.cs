using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class PlayerService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ILogger<PlayerService> logger)
{
    /// <summary>Everyone on file, in shirt order. Guest status is per season now, so the
    /// guests-last ordering moved to <see cref="SeasonSquad"/>, which is the only thing that can
    /// know it.
    /// <para>
    /// Archived players are included, and that is not an oversight. This is the lookup the pages
    /// resolve a player id against, so filtering here would blank the scorer out of a match report
    /// they actually scored in. What archiving takes someone out of is the pickers —
    /// <see cref="SeasonSquadService.GetNonMembersAsync"/> and
    /// <see cref="SeasonSquadService.CopyFromAsync"/> — not the past.
    /// </para></summary>
    public Task<Result<List<Player>>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load players", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var players = await db.Players
                .AsNoTracking()
                .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
                .ThenBy(p => p.FirstName)
                .ThenBy(p => p.Surname)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} players", players.Count);
            return Result.Success(players);
        });

    public Task<Result<Player>> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load player", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var player = await db.Players.FindAsync([id], cancellationToken);
            if (player is null)
            {
                logger.LogWarning("Player {PlayerId} not found", id);
                return Result.Failure<Player>("Player with ID {0} not found", id);
            }

            return Result.Success(player);
        });

    public Task<Result<Player>> CreateAsync(Player player, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create player", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            db.Players.Add(player);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
            return Result.Success(player);
        });

    public Task<Result> UpdateAsync(Player player, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update player", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            db.Players.Update(player);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
            return Result.Success();
        });

    /// <summary>
    /// Retires someone from the club, or brings them back. The person's rows are not touched, so
    /// the seasons they played read exactly as they did — see <see cref="Player.IsArchived"/>.
    /// </summary>
    public Task<Result> SetArchivedAsync(int id, bool archived, CancellationToken cancellationToken = default) =>
        // "archive the player", not "archive player": resx keys are case-insensitive and the menu
        // item on /players is "Archive player", which would be the same key. See known_issues.md.
        ServiceOperation.RunAdminAsync(currentUser, logger, "archive the player", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var player = await db.Players.FindAsync([id], cancellationToken);
            if (player is null)
            {
                logger.LogWarning("Cannot archive player {PlayerId}: not found", id);
                return Result.Failure("Player not found");
            }

            player.IsArchived = archived;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("{PlayerName} (ID: {PlayerId}) is now {Status}",
                player.DisplayName, player.Id, archived ? "archived" : "active");
            return Result.Success();
        });

    /// <summary>
    /// Deletes the person outright — and refuses once that would take a season's history with them.
    /// <para>
    /// A player's lineup and goal rows cascade from this row, so deleting last season's top scorer
    /// used to remove them from last season's table as well: a silent edit to a season nobody was
    /// looking at. Both counts below are unscoped by season on purpose, because the damage is too.
    /// Archiving is the way out for anyone who has played, and delete stays available for the case
    /// it is actually for — a mistyped name added minutes ago, with nothing behind it yet.
    /// </para>
    /// </summary>
    public Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "delete player", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var player = await db.Players.FindAsync([id], cancellationToken);
            if (player is null)
            {
                logger.LogWarning("Cannot delete player {PlayerId}: not found", id);
                return Result.Failure("Player not found");
            }

            var appearances = await db.GamePlayerPositions.CountAsync(pp => pp.PlayerId == id, cancellationToken);
            var contributions = await db.GameGoals.CountAsync(g => g.ScorerId == id || g.AssisterId == id, cancellationToken);
            if (appearances + contributions > 0)
            {
                logger.LogWarning(
                    "Refused to delete {PlayerName} (ID: {PlayerId}): {Appearances} lineup and {Goals} goal entries",
                    player.DisplayName, id, appearances, contributions);
                return Result.Failure("{0} has already played — archive them instead of deleting them",
                    player.DisplayName);
            }

            db.Players.Remove(player);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted player {PlayerName} (ID: {PlayerId})", player.DisplayName, player.Id);
            return Result.Success();
        });
}

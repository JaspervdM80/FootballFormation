namespace FootballFormation.Core.Services;

public class PlayerService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ILogger<PlayerService> logger)
{
    /// Archived players are included on purpose: this is the lookup pages resolve a player id against, and filtering here would blank
    /// the scorer out of a match report she actually scored in. Archiving takes someone out of the pickers, not the past.
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

    /// Touches no other row, so the seasons she played read exactly as they did — see <see cref="Player.IsArchived"/>.
    public Task<Result> SetArchivedAsync(int id, bool archived, CancellationToken cancellationToken = default) =>
        // "archive the player", not "archive player": resx keys are case-insensitive, and /players already has an "Archive player" menu
        // item that would collide. See docs/known_issues/localization.md.
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

    /// Line-up and goal rows cascade from this one, so deleting last season's top scorer would silently edit last season's table. The
    /// counts below are unscoped by season because the damage is too; archiving is the way out for anyone who has played.
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

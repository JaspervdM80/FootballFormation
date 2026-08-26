namespace FootballFormation.Core.Services;

/// Separate from <see cref="SeasonService"/>, which owns the season lifecycle, and takes no service dependency of its own — so
/// GameService → SeasonService stays the only service-to-service edge.
public class SeasonSquadService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ILogger<SeasonSquadService> logger)
{
    /// An empty squad is a valid answer, not a failure — a new season has none until it is copied forward or filled in.
    public Task<Result<SeasonSquad>> GetSquadAsync(int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var members = await db.SeasonSquadMembers
                .AsNoTracking()
                .Where(m => m.SeasonId == seasonId)
                .Include(m => m.Player)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} squad members for season {SeasonId}", members.Count, seasonId);
            return Result.Success(new SeasonSquad(seasonId, members));
        });

    /// A null <paramref name="seasonId"/> loads every season, which is what "All seasons" needs to walk games across them.
    public Task<Result<SeasonSquads>> GetSquadsAsync(
        int? seasonId = null, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the squads", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var members = await db.SeasonSquadMembers
                .AsNoTracking()
                .Where(m => seasonId == null || m.SeasonId == seasonId)
                .Include(m => m.Player)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} squad members for season {SeasonId}", members.Count, seasonId);
            return Result.Success(new SeasonSquads(members));
        });

    /// Archived players are left out — offering them in the "add existing player" picker is what would make archiving pointless.
    public Task<Result<List<Player>>> GetNonMembersAsync(
        int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load players outside the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var players = await db.Players
                .AsNoTracking()
                .Where(p => !p.IsArchived)
                .Where(p => !db.SeasonSquadMembers.Any(m => m.SeasonId == seasonId && m.PlayerId == p.Id))
                .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
                .ThenBy(p => p.FirstName)
                .ThenBy(p => p.Surname)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} players outside season {SeasonId}", players.Count, seasonId);
            return Result.Success(players);
        });

    public Task<Result<SeasonSquadMember>> AddMemberAsync(
        int seasonId, int playerId, bool isGuest = false, bool isInjured = false,
        CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "add the player to the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var season = await db.Seasons.FindAsync([seasonId], cancellationToken);
            if (season is null)
            {
                logger.LogWarning("Cannot add player {PlayerId}: season {SeasonId} not found", playerId, seasonId);
                return Result.Failure<SeasonSquadMember>("Season not found");
            }

            var player = await db.Players.FindAsync([playerId], cancellationToken);
            if (player is null)
            {
                logger.LogWarning("Cannot add player {PlayerId} to season {SeasonName}: player not found",
                    playerId, season.Name);
                return Result.Failure<SeasonSquadMember>("Player not found");
            }

            // The unique index is the net; refuse here so the caller gets something readable instead of a raw DbUpdateException.
            var exists = await db.SeasonSquadMembers
                .AnyAsync(m => m.SeasonId == seasonId && m.PlayerId == playerId, cancellationToken);
            if (exists)
            {
                logger.LogWarning("Player {PlayerName} is already in the {SeasonName} squad",
                    player.DisplayName, season.Name);
                return Result.Failure<SeasonSquadMember>("{0} is already in the {1} squad", player.DisplayName, season.Name);
            }

            var member = new SeasonSquadMember
            {
                SeasonId = seasonId, PlayerId = playerId, IsGuest = isGuest, IsInjured = isInjured
            };
            db.SeasonSquadMembers.Add(member);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Added {PlayerName} to the {SeasonName} squad (guest: {IsGuest}, injured: {IsInjured})",
                player.DisplayName, season.Name, isGuest, isInjured);
            return Result.Success(member);
        });

    /// Refuses once the player has minutes or goals in this season, because removing them would silently rewrite that season's stats.
    public Task<Result> RemoveMemberAsync(
        int seasonId, int playerId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "remove the player from the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var member = await FindMemberAsync(db, seasonId, playerId, cancellationToken);
            if (member is null)
            {
                logger.LogWarning("Cannot remove player {PlayerId} from season {SeasonId}: not in the squad",
                    playerId, seasonId);
                return NotInSquad();
            }

            var name = member.Player?.DisplayName ?? $"Player {playerId}";

            var appearances = await db.GamePlayerPositions
                .CountAsync(pp => pp.PlayerId == playerId && pp.GamePeriod!.Game!.SeasonId == seasonId, cancellationToken);
            if (appearances > 0)
            {
                logger.LogWarning("Cannot remove {PlayerName} from season {SeasonId}: {Count} lineup entries",
                    name, seasonId, appearances);
                return Result.Failure("{0} has already played this season", name);
            }

            var contributions = await db.GameGoals
                .CountAsync(g => (g.ScorerId == playerId || g.AssisterId == playerId)
                                 && g.Game!.SeasonId == seasonId, cancellationToken);
            if (contributions > 0)
            {
                logger.LogWarning("Cannot remove {PlayerName} from season {SeasonId}: {Count} goal entries",
                    name, seasonId, contributions);
                return Result.Failure("{0} has goals or assists this season", name);
            }

            db.SeasonSquadMembers.Remove(member);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Removed {PlayerName} from the squad of season {SeasonId}", name, seasonId);
            return Result.Success();
        });

    public Task<Result> SetGuestAsync(
        int seasonId, int playerId, bool isGuest, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "change the squad status", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var member = await FindMemberAsync(db, seasonId, playerId, cancellationToken);
            if (member is null)
            {
                logger.LogWarning("Cannot change status of player {PlayerId} in season {SeasonId}: not in the squad",
                    playerId, seasonId);
                return NotInSquad();
            }

            member.IsGuest = isGuest;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("{PlayerName} is now a {Status} in season {SeasonId}",
                member.Player?.DisplayName, isGuest ? "guest" : "squad player", seasonId);
            return Result.Success();
        });

    public Task<Result> SetInjuredAsync(
        int seasonId, int playerId, bool isInjured, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "change the squad status", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var member = await FindMemberAsync(db, seasonId, playerId, cancellationToken);
            if (member is null)
            {
                logger.LogWarning("Cannot change status of player {PlayerId} in season {SeasonId}: not in the squad",
                    playerId, seasonId);
                return NotInSquad();
            }

            member.IsInjured = isInjured;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("{PlayerName} is {Status} in season {SeasonId}",
                member.Player?.DisplayName, isInjured ? "now injured" : "no longer injured", seasonId);
            return Result.Success();
        });

    /// Guest status carries over but injury does not: a standing arrangement survives the summer, a temporary condition should have
    /// healed. Archived players are skipped, or copying forward would undo their archiving every time a season is set up.
    public Task<Result<int>> CopyFromAsync(
        int fromSeasonId, int toSeasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "copy the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (fromSeasonId == toSeasonId)
            {
                logger.LogWarning("Cannot copy squad {SeasonId} onto itself", fromSeasonId);
                return Result.Failure<int>("Cannot copy a squad onto itself");
            }

            var source = await db.Seasons.FindAsync([fromSeasonId], cancellationToken);
            var target = await db.Seasons.FindAsync([toSeasonId], cancellationToken);
            if (source is null || target is null)
            {
                logger.LogWarning("Cannot copy squad {From} -> {To}: season not found", fromSeasonId, toSeasonId);
                return Result.Failure<int>("Season not found");
            }

            var sourceMembers = await db.SeasonSquadMembers
                .Where(m => m.SeasonId == fromSeasonId)
                .ToListAsync(cancellationToken);

            if (sourceMembers.Count == 0)
            {
                logger.LogWarning("Cannot copy squad from {SeasonName}: it has no members", source.Name);
                return Result.Failure<int>("Season {0} has no squad to copy", source.Name);
            }

            var already = await db.SeasonSquadMembers
                .Where(m => m.SeasonId == toSeasonId)
                .Select(m => m.PlayerId)
                .ToListAsync(cancellationToken);
            var existing = already.ToHashSet();

            var archived = await db.Players
                .Where(p => p.IsArchived)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            var left = archived.ToHashSet();

            var added = sourceMembers
                .Where(m => !existing.Contains(m.PlayerId) && !left.Contains(m.PlayerId))
                .Select(m => new SeasonSquadMember
                {
                    SeasonId = toSeasonId,
                    PlayerId = m.PlayerId,
                    IsGuest = m.IsGuest
                })
                .ToList();

            db.SeasonSquadMembers.AddRange(added);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Copied {Count} squad members from {From} to {To} ({Skipped} already there or archived)",
                added.Count, source.Name, target.Name, sourceMembers.Count - added.Count);
            return Result.Success(added.Count);
        });

    /// A null value on a successful result means there is no earlier season — a normal state, not an error.
    public Task<Result<Season?>> FindPreviousSeasonAsync(
        int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "find the previous season", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var season = await db.Seasons.FindAsync([seasonId], cancellationToken);
            if (season is null)
            {
                logger.LogWarning("Cannot find the previous season: season {SeasonId} not found", seasonId);
                return Result.Failure<Season?>("Season not found");
            }

            var previous = (await db.Seasons.AsNoTracking().ToListAsync(cancellationToken))
                .NewestFirst()
                .FirstOrDefault(s => s.StartDate.Date < season.StartDate.Date);

            return Result.Success<Season?>(previous);
        });

    /// Tracked, and with the player loaded — the log lines and failure messages name a person rather than an id.
    private static Task<SeasonSquadMember?> FindMemberAsync(
        AppDbContext db, int seasonId, int playerId, CancellationToken cancellationToken) =>
        db.SeasonSquadMembers
            .Include(m => m.Player)
            .FirstOrDefaultAsync(m => m.SeasonId == seasonId && m.PlayerId == playerId, cancellationToken);

    // One message, so every write says — and translates to — the same thing.
    private static Result NotInSquad() => Result.Failure("Player is not in this squad");
}

using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// The season squad — who can be picked for a season's games, and who is only a guest.
/// Deliberately separate from <see cref="SeasonService"/>: that owns the season lifecycle and the
/// "exactly one current season" invariant, this owns membership. It takes no service dependency,
/// so <c>GameService -&gt; SeasonService</c> stays the only service-to-service edge.
/// </summary>
public class SeasonSquadService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ILogger<SeasonSquadService> logger)
{
    /// <summary>One season's squad, players loaded. An empty squad is a valid answer — a new
    /// season has none until it is copied forward or filled in.</summary>
    public Task<Result<SeasonSquad>> GetSquadAsync(int seasonId) =>
        ServiceOperation.RunAsync(logger, "load the squad", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var members = await db.SeasonSquadMembers
                .AsNoTracking()
                .Where(m => m.SeasonId == seasonId)
                .Include(m => m.Player)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} squad members for season {SeasonId}", members.Count, seasonId);
            return Result.Success(new SeasonSquad(seasonId, members));
        });

    /// <param name="seasonId">Limits the result to one season. Null loads every season — what the
    /// stats pages need on "All seasons", where a report walks games across them.</param>
    public Task<Result<SeasonSquads>> GetSquadsAsync(int? seasonId = null) =>
        ServiceOperation.RunAsync(logger, "load the squads", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var members = await db.SeasonSquadMembers
                .AsNoTracking()
                .Where(m => seasonId == null || m.SeasonId == seasonId)
                .Include(m => m.Player)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} squad members for season {SeasonId}", members.Count, seasonId);
            return Result.Success(new SeasonSquads(members));
        });

    /// <summary>People on file who are not in this season's squad — the "add existing player" picker.</summary>
    public Task<Result<List<Player>>> GetNonMembersAsync(int seasonId) =>
        ServiceOperation.RunAsync(logger, "load players outside the squad", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var players = await db.Players
                .AsNoTracking()
                .Where(p => !db.SeasonSquadMembers.Any(m => m.SeasonId == seasonId && m.PlayerId == p.Id))
                .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
                .ThenBy(p => p.FirstName)
                .ThenBy(p => p.Surname)
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} players outside season {SeasonId}", players.Count, seasonId);
            return Result.Success(players);
        });

    public Task<Result<SeasonSquadMember>> AddMemberAsync(int seasonId, int playerId, bool isGuest = false) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "add the player to the squad", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var season = await db.Seasons.FindAsync(seasonId);
            if (season is null)
            {
                logger.LogWarning("Cannot add player {PlayerId}: season {SeasonId} not found", playerId, seasonId);
                return Result.Failure<SeasonSquadMember>("Season not found");
            }

            var player = await db.Players.FindAsync(playerId);
            if (player is null)
            {
                logger.LogWarning("Cannot add player {PlayerId} to season {SeasonName}: player not found",
                    playerId, season.Name);
                return Result.Failure<SeasonSquadMember>("Player not found");
            }

            // The unique index is the net; refuse here so the caller gets something readable
            // instead of a raw DbUpdateException.
            var exists = await db.SeasonSquadMembers
                .AnyAsync(m => m.SeasonId == seasonId && m.PlayerId == playerId);
            if (exists)
            {
                logger.LogWarning("Player {PlayerName} is already in the {SeasonName} squad",
                    player.DisplayName, season.Name);
                return Result.Failure<SeasonSquadMember>("{0} is already in the {1} squad", player.DisplayName, season.Name);
            }

            var member = new SeasonSquadMember { SeasonId = seasonId, PlayerId = playerId, IsGuest = isGuest };
            db.SeasonSquadMembers.Add(member);
            await db.SaveChangesAsync();

            logger.LogInformation("Added {PlayerName} to the {SeasonName} squad (guest: {IsGuest})",
                player.DisplayName, season.Name, isGuest);
            return Result.Success(member);
        });

    /// <summary>Refuses once the player has recorded minutes or goals in this season — removing
    /// them would silently rewrite that season's stats.</summary>
    public Task<Result> RemoveMemberAsync(int seasonId, int playerId) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "remove the player from the squad", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var member = await db.SeasonSquadMembers
                .Include(m => m.Player)
                .FirstOrDefaultAsync(m => m.SeasonId == seasonId && m.PlayerId == playerId);

            if (member is null)
            {
                logger.LogWarning("Cannot remove player {PlayerId} from season {SeasonId}: not in the squad",
                    playerId, seasonId);
                return Result.Failure("Player is not in this squad");
            }

            var name = member.Player?.DisplayName ?? $"Player {playerId}";

            var appearances = await db.GamePlayerPositions
                .CountAsync(pp => pp.PlayerId == playerId && pp.GamePeriod!.Game!.SeasonId == seasonId);
            if (appearances > 0)
            {
                logger.LogWarning("Cannot remove {PlayerName} from season {SeasonId}: {Count} lineup entries",
                    name, seasonId, appearances);
                return Result.Failure("{0} has already played this season", name);
            }

            var contributions = await db.GameGoals
                .CountAsync(g => (g.ScorerId == playerId || g.AssisterId == playerId)
                                 && g.Game!.SeasonId == seasonId);
            if (contributions > 0)
            {
                logger.LogWarning("Cannot remove {PlayerName} from season {SeasonId}: {Count} goal entries",
                    name, seasonId, contributions);
                return Result.Failure("{0} has goals or assists this season", name);
            }

            db.SeasonSquadMembers.Remove(member);
            await db.SaveChangesAsync();

            logger.LogInformation("Removed {PlayerName} from the squad of season {SeasonId}", name, seasonId);
            return Result.Success();
        });

    public Task<Result> SetGuestAsync(int seasonId, int playerId, bool isGuest) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "change the squad status", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var member = await db.SeasonSquadMembers
                .Include(m => m.Player)
                .FirstOrDefaultAsync(m => m.SeasonId == seasonId && m.PlayerId == playerId);

            if (member is null)
            {
                logger.LogWarning("Cannot change status of player {PlayerId} in season {SeasonId}: not in the squad",
                    playerId, seasonId);
                return Result.Failure("Player is not in this squad");
            }

            member.IsGuest = isGuest;
            await db.SaveChangesAsync();

            logger.LogInformation("{PlayerName} is now a {Status} in season {SeasonId}",
                member.Player?.DisplayName, isGuest ? "guest" : "squad player", seasonId);
            return Result.Success();
        });

    /// <summary>
    /// Populates a season's squad from another one, preserving guest status. Idempotent — players
    /// already in the target are skipped, so running it twice adds nothing.
    /// </summary>
    /// <returns>How many members were added.</returns>
    public Task<Result<int>> CopyFromAsync(int fromSeasonId, int toSeasonId) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "copy the squad", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (fromSeasonId == toSeasonId)
            {
                logger.LogWarning("Cannot copy squad {SeasonId} onto itself", fromSeasonId);
                return Result.Failure<int>("Cannot copy a squad onto itself");
            }

            var source = await db.Seasons.FindAsync(fromSeasonId);
            var target = await db.Seasons.FindAsync(toSeasonId);
            if (source is null || target is null)
            {
                logger.LogWarning("Cannot copy squad {From} -> {To}: season not found", fromSeasonId, toSeasonId);
                return Result.Failure<int>("Season not found");
            }

            var sourceMembers = await db.SeasonSquadMembers
                .Where(m => m.SeasonId == fromSeasonId)
                .ToListAsync();

            if (sourceMembers.Count == 0)
            {
                logger.LogWarning("Cannot copy squad from {SeasonName}: it has no members", source.Name);
                return Result.Failure<int>("Season {0} has no squad to copy", source.Name);
            }

            var already = await db.SeasonSquadMembers
                .Where(m => m.SeasonId == toSeasonId)
                .Select(m => m.PlayerId)
                .ToListAsync();
            var existing = already.ToHashSet();

            var added = sourceMembers
                .Where(m => !existing.Contains(m.PlayerId))
                .Select(m => new SeasonSquadMember
                {
                    SeasonId = toSeasonId,
                    PlayerId = m.PlayerId,
                    IsGuest = m.IsGuest
                })
                .ToList();

            db.SeasonSquadMembers.AddRange(added);
            await db.SaveChangesAsync();

            logger.LogInformation("Copied {Count} squad members from {From} to {To} ({Skipped} already present)",
                added.Count, source.Name, target.Name, sourceMembers.Count - added.Count);
            return Result.Success(added.Count);
        });

    /// <summary>The season immediately before this one, for the copy-forward offer. A null value
    /// with a successful result means there is no earlier season — a normal state, not an error.</summary>
    public Task<Result<Season?>> FindPreviousSeasonAsync(int seasonId) =>
        ServiceOperation.RunAsync(logger, "find the previous season", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var season = await db.Seasons.FindAsync(seasonId);
            if (season is null)
            {
                logger.LogWarning("Cannot find the previous season: season {SeasonId} not found", seasonId);
                return Result.Failure<Season?>("Season not found");
            }

            var previous = await db.Seasons
                .AsNoTracking()
                .Where(s => s.StartDate < season.StartDate)
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            return Result.Success<Season?>(previous);
        });
}

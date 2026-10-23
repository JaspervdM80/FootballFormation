namespace FootballFormation.Core.Services;

// Everyone on file as well as the squad: a player who appeared in a match stays nameable on it, whatever her membership is now.
public sealed record MatchPeople(SeasonSquad Squad, List<Player> Everyone)
{
    public static MatchPeople None => new(SeasonSquad.Empty, []);
}

/// Separate from <see cref="SeasonService"/>, which owns the season lifecycle, and takes no service dependency of its own — so
/// GameService → SeasonService stays the only service-to-service edge.
public class SeasonSquadService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<SeasonSquadService> logger)
{
    /// An empty squad is a valid answer, not a failure — a new season has none until it is copied forward or filled in.
    public Task<Result<SeasonSquad>> GetSquadAsync(int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var members = await db.SquadOf(seasonId).ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} squad members for season {SeasonId}", members.Count, seasonId);
            return Result.Success(new SeasonSquad(seasonId, members));
        });

    /// What every match page reads alongside its game, in one context rather than two services' worth.
    public Task<Result<MatchPeople>> GetMatchPeopleAsync(int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var members = await db.SquadOf(seasonId).ToListAsync(cancellationToken);

            var everyone = await db.Players
                .AsNoTracking()
                .InSquadOrder()
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} squad members of {Everyone} players for season {SeasonId}",
                members.Count, everyone.Count, seasonId);
            return Result.Success(new MatchPeople(new SeasonSquad(seasonId, members), everyone));
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

    /// Who the "existing player" picker offers and a pasted name is matched against: players are the club's, but only those who were in
    /// one of this team's squads count. Archived players are left out, or archiving them would be pointless.
    public Task<Result<List<Player>>> GetFormerMembersAsync(
        int seasonId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the team's earlier players", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var players = await FormerMembers(db, seasonId).AsNoTracking().ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} earlier players outside season {SeasonId}", players.Count, seasonId);
            return Result.Success(players);
        });

    /// All or nothing: one save, so a refusal for any one player leaves the squad as it was.
    public Task<Result<int>> AddMembersAsync(
        int seasonId, IReadOnlyCollection<int> playerIds, bool isGuest = false, bool isInjured = false,
        CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "add the players to the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, cancellationToken);
            if (season is null)
            {
                logger.LogWarning("Cannot add players: season {SeasonId} not found", seasonId);
                return Result.Failure<int>("Season not found");
            }

            var ids = playerIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                logger.LogWarning("Cannot add players to the {SeasonName} squad: none picked", season.Name);
                return Result.Failure<int>("Pick at least one player");
            }

            var players = await db.Players.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
            if (players.Count != ids.Count)
            {
                logger.LogWarning("Cannot add players to the {SeasonName} squad: {Missing} not found",
                    season.Name, ids.Count - players.Count);
                return Result.Failure<int>("Player not found");
            }

            var already = await db.SeasonSquadMembers
                .Where(m => m.SeasonId == seasonId && ids.Contains(m.PlayerId))
                .Select(m => m.PlayerId)
                .FirstOrDefaultAsync(cancellationToken);
            if (already != 0)
            {
                var name = players.First(p => p.Id == already).DisplayName;
                logger.LogWarning("Player {PlayerName} is already in the {SeasonName} squad", name, season.Name);
                return Result.Failure<int>("{0} is already in the {1} squad", name, season.Name);
            }

            var injuredSince = isInjured ? time.GetLocalNow().Date : (DateTime?)null;
            db.SeasonSquadMembers.AddRange(players.Select(p => MemberOf(season, p, isGuest, injuredSince)));
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Added {Count} players to the {SeasonName} squad (guest: {IsGuest}, injured: {IsInjured})",
                players.Count, season.Name, isGuest, isInjured);
            return Result.Success(players.Count);
        });

    /// Re-plans at save time and refuses a list with any flagged line — the service, not the dialog, is the guard.
    public Task<Result<PastedSquadOutcome>> AddPastedListAsync(
        int seasonId, string text, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "add the pasted list to the squad", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, cancellationToken);
            if (season is null)
            {
                logger.LogWarning("Cannot add a pasted list: season {SeasonId} not found", seasonId);
                return Result.Failure<PastedSquadOutcome>("Season not found");
            }

            var squadPlayers = await db.SeasonSquadMembers
                .Where(m => m.SeasonId == seasonId)
                .Select(m => m.Player!)
                .ToListAsync(cancellationToken);
            var earlierPlayers = await FormerMembers(db, seasonId).ToListAsync(cancellationToken);

            var plan = PastedSquadList.Plan(text, squadPlayers, earlierPlayers);
            if (plan.Count == 0)
            {
                logger.LogWarning("Cannot add a pasted list to the {SeasonName} squad: it is empty", season.Name);
                return Result.Failure<PastedSquadOutcome>("The list has no players in it");
            }

            if (plan.FirstOrDefault(e => !e.IsValid) is { } invalid)
            {
                logger.LogWarning("Cannot add a pasted list to the {SeasonName} squad: line {Line} is {Problem}",
                    season.Name, invalid.LineNumber, invalid.Problem);
                return Result.Failure<PastedSquadOutcome>("Line {0} of the list cannot be added", invalid.LineNumber);
            }

            List<Player> Players(PastedLineAction action) => [.. plan.Where(e => e.Action == action).Select(e => e.Player)];
            var created = Players(PastedLineAction.Create);
            var reused = Players(PastedLineAction.AddExisting);
            var skipped = Players(PastedLineAction.AlreadyInSquad);

            foreach (var player in created) player.ClubId = db.CurrentClubId!.Value;
            db.SeasonSquadMembers.AddRange(created.Concat(reused).Select(p => MemberOf(season, p)));
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Added a pasted list to the {SeasonName} squad: {Created} created, {Reused} from an earlier season, {Skipped} already in it",
                season.Name, created.Count, reused.Count, skipped.Count);
            return Result.Success(new PastedSquadOutcome(created, reused, skipped));
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

            // Left alone when the flag is already on, or re-saving the squad would move the injury forward and un-stamp the sessions
            // she has already missed.
            if (isInjured) member.InjuredSince ??= time.GetLocalNow().Date;
            else member.InjuredSince = null;

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

            // FirstOrDefault, not Find: Find bypasses the query filter, so copying to or from another team's season stays impossible.
            var source = await db.Seasons.FirstOrDefaultAsync(s => s.Id == fromSeasonId, cancellationToken);
            var target = await db.Seasons.FirstOrDefaultAsync(s => s.Id == toSeasonId, cancellationToken);
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
                    TeamId = target.TeamId,
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

            var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, cancellationToken);
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

    // Through the navigation rather than the id, so a player created in the same save gets her id from it.
    private static SeasonSquadMember MemberOf(Season season, Player player, bool isGuest = false, DateTime? injuredSince = null) =>
        new()
        {
            SeasonId = season.Id, TeamId = season.TeamId, Player = player, IsGuest = isGuest,
            IsInjured = injuredSince is not null, InjuredSince = injuredSince
        };

    // The membership filter is the team's, so this is who has played for this team before; ordered so the preview and the write
    // resolve a name two of them share to the same one.
    private static IQueryable<Player> FormerMembers(AppDbContext db, int seasonId) =>
        db.Players
            .Where(p => !p.IsArchived)
            .Where(p => db.SeasonSquadMembers.Any(m => m.PlayerId == p.Id))
            .Where(p => !db.SeasonSquadMembers.Any(m => m.SeasonId == seasonId && m.PlayerId == p.Id))
            .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
            .ThenBy(p => p.FirstName)
            .ThenBy(p => p.Surname)
            .ThenBy(p => p.Id);

    /// Tracked, and with the player loaded — the log lines and failure messages name a person rather than an id.
    private static Task<SeasonSquadMember?> FindMemberAsync(
        AppDbContext db, int seasonId, int playerId, CancellationToken cancellationToken) =>
        db.SeasonSquadMembers
            .Include(m => m.Player)
            .FirstOrDefaultAsync(m => m.SeasonId == seasonId && m.PlayerId == playerId, cancellationToken);

    // One message, so every write says — and translates to — the same thing.
    private static Result NotInSquad() => Result.Failure("Player is not in this squad");
}

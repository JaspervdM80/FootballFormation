namespace FootballFormation.Core.Services;

/// The clubs and teams the app serves. Reads are public like every other read; writes are the one thing an ordinary admin may not do,
/// because they are about the app rather than about a season.
public class TeamService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ICurrentTeam currentTeam,
    ILogger<TeamService> logger)
{
    public Task<Result<List<Club>>> GetClubsAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load clubs", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var clubs = await db.Clubs
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} clubs", clubs.Count);
            return Result.Success(clubs);
        });

    public Task<Result<List<Team>>> GetTeamsAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load teams", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var teams = await db.Teams
                .AsNoTracking()
                .Include(t => t.Club)
                .OrderBy(t => t.Club!.Name)
                .ThenBy(t => t.Name)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} teams", teams.Count);
            return Result.Success(teams);
        });

    /// The team the app is showing this visitor: what they last looked at, or the first team there is. Null before seeding.
    public Task<Result<Team?>> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load the current team", cancellationToken, async () =>
        {
            var currentId = await currentTeam.GetIdAsync();
            if (currentId is null) return Result.Success<Team?>(null);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var team = await db.Teams
                .AsNoTracking()
                .Include(t => t.Club)
                .FirstOrDefaultAsync(t => t.Id == currentId, cancellationToken);

            return Result.Success(team);
        });

    public Task<Result<Club>> CreateClubAsync(Club club, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunApplicationAdminAsync(currentUser, logger, "create the club", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var validation = await ValidateClubAsync(db, club, cancellationToken);
            if (validation.IsFailure) return validation.To<Club>();

            var created = new Club
            {
                Name = club.Name.Trim(),
                LogoUrl = Blank(club.LogoUrl),
                ThemeName = ThemeOr(club.ThemeName)
            };

            db.Clubs.Add(created);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created club {ClubName} (ID: {ClubId})", created.Name, created.Id);
            return Result.Success(created);
        });

    public Task<Result> UpdateClubAsync(Club club, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunApplicationAdminAsync(currentUser, logger, "update the club", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var existing = await db.Clubs.FirstOrDefaultAsync(c => c.Id == club.Id, cancellationToken);
            if (existing is null) return ClubNotFound(club.Id);

            var validation = await ValidateClubAsync(db, club, cancellationToken);
            if (validation.IsFailure) return validation;

            existing.Name = club.Name.Trim();
            existing.LogoUrl = Blank(club.LogoUrl);
            existing.ThemeName = ThemeOr(club.ThemeName);

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated club {ClubName} (ID: {ClubId})", existing.Name, existing.Id);
            return Result.Success();
        });

    public Task<Result> DeleteClubAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunApplicationAdminAsync(currentUser, logger, "delete the club", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (club is null) return ClubNotFound(id);

            // The FK is Restrict, so refuse here rather than letting the caller hit a raw DbUpdateException.
            var teams = await db.Teams.CountAsync(t => t.ClubId == id, cancellationToken);
            if (teams > 0)
            {
                logger.LogWarning("Cannot delete club {ClubName}: {Count} teams still assigned", club.Name, teams);
                return Result.Failure("Club {0} still has {1} teams", club.Name, teams);
            }

            db.Clubs.Remove(club);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted club {ClubName} (ID: {ClubId})", club.Name, id);
            return Result.Success();
        });

    public Task<Result<Team>> CreateTeamAsync(Team team, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunApplicationAdminAsync(currentUser, logger, "create the team", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var validation = await ValidateTeamAsync(db, team, cancellationToken);
            if (validation.IsFailure) return validation.To<Team>();

            var created = new Team { ClubId = team.ClubId, Name = team.Name.Trim() };

            db.Teams.Add(created);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created team {TeamName} for club {ClubId} (ID: {TeamId})",
                created.Name, created.ClubId, created.Id);
            return Result.Success(created);
        });

    public Task<Result> UpdateTeamAsync(Team team, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunApplicationAdminAsync(currentUser, logger, "update the team", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var existing = await db.Teams.FirstOrDefaultAsync(t => t.Id == team.Id, cancellationToken);
            if (existing is null) return TeamNotFound(team.Id);

            var validation = await ValidateTeamAsync(db, team, cancellationToken);
            if (validation.IsFailure) return validation;

            existing.ClubId = team.ClubId;
            existing.Name = team.Name.Trim();

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated team {TeamName} (ID: {TeamId})", existing.Name, existing.Id);
            return Result.Success();
        });

    public Task<Result> DeleteTeamAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunApplicationAdminAsync(currentUser, logger, "delete the team", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (team is null) return TeamNotFound(id);

            // Not merely the last team: a visitor who has chosen none falls back to the lowest id, so removing the one it names would
            // silently move the app — its title, its crest and its manifest — onto a different team while every season and game stayed
            // put. The only team is always that fallback, so this covers that case too.
            var fallback = await db.Teams.OrderBy(t => t.Id).FirstOrDefaultAsync(cancellationToken);
            if (fallback?.Id == id)
            {
                logger.LogWarning("Cannot delete team {TeamName}: it is the team the app falls back to", team.Name);
                return Result.Failure("{0} is the team the app is showing", team.Name);
            }

            // The FK is Restrict, so refuse here rather than letting the caller hit a raw DbUpdateException.
            var admins = await db.Users.CountAsync(u => u.TeamId == id, cancellationToken);
            if (admins > 0)
            {
                logger.LogWarning("Cannot delete team {TeamName}: {Count} accounts still run it", team.Name, admins);
                return Result.Failure("Team {0} still has {1} accounts", team.Name, admins);
            }

            db.Teams.Remove(team);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted team {TeamName} (ID: {TeamId})", team.Name, id);
            return Result.Success();
        });

    /// Runs every boot and does nothing once any club exists, so a renamed club is never overwritten. Without it a database that
    /// predates teams has none, and GetCurrentAsync answers null forever.
    public async Task EnsureSeededAsync(
        string clubName, string teamName, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Clubs.AnyAsync(cancellationToken)) return;

        var club = new Club { Name = clubName };
        club.Teams.Add(new Team { Name = teamName, Club = club });

        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded club {ClubName} with team {TeamName}", clubName, teamName);
    }

    private async Task<Result> ValidateClubAsync(AppDbContext db, Club club, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(club.Name))
        {
            logger.LogWarning("Rejected club {ClubId}: name is empty", club.Id);
            return Result.Failure("Club name is required");
        }

        if (club.Name.Trim().Length > 100) return Result.Failure("Club name must be at most {0} characters", 100);

        var logo = Blank(club.LogoUrl);
        if (logo is not null && !IsAppPath(logo))
        {
            logger.LogWarning("Rejected club {ClubName}: logo {Logo} is not a path inside the app", club.Name, logo);
            return Result.Failure(LogoNotLocalKey);
        }

        var name = club.Name.Trim();
        if (await db.Clubs.AnyAsync(c => c.Name == name && c.Id != club.Id, cancellationToken))
        {
            logger.LogWarning("Rejected club {ClubName}: a club by that name already exists", name);
            return Result.Failure("A club named {0} already exists", name);
        }

        return Result.Success();
    }

    private async Task<Result> ValidateTeamAsync(AppDbContext db, Team team, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(team.Name))
        {
            logger.LogWarning("Rejected team {TeamId}: name is empty", team.Id);
            return Result.Failure("Team name is required");
        }

        if (team.Name.Trim().Length > 50) return Result.Failure("Team name must be at most {0} characters", 50);

        var club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == team.ClubId, cancellationToken);
        if (club is null)
        {
            logger.LogWarning("Rejected team {TeamName}: club {ClubId} not found", team.Name, team.ClubId);
            return ClubNotFound(team.ClubId);
        }

        var name = team.Name.Trim();
        if (await db.Teams.AnyAsync(t => t.ClubId == team.ClubId && t.Name == name && t.Id != team.Id, cancellationToken))
        {
            logger.LogWarning("Rejected team {TeamName}: {ClubName} already has one", name, club.Name);
            return Result.Failure("{0} already has a team named {1}", club.Name, name);
        }

        return Result.Success();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// The crest renders into an img on every page for every visitor, so an absolute URL here would have the whole audience fetching a
    /// third party. Same two prefixes Routing.IsLocalUrl refuses, for the same reason.
    private static bool IsAppPath(string path) =>
        !path.StartsWith("//") && !path.StartsWith("/\\") && Uri.TryCreate(path, UriKind.Relative, out _);

    private static string ThemeOr(string? themeName) =>
        string.IsNullOrWhiteSpace(themeName) ? Club.DefaultTheme : themeName.Trim();

    private const string LogoNotLocalKey = "The logo must be a path inside the app, like icons/icon-192.png";

    private static Result ClubNotFound(int id) => Result.Failure("Club with ID {0} not found", id);

    private static Result TeamNotFound(int id) => Result.Failure("Team with ID {0} not found", id);
}

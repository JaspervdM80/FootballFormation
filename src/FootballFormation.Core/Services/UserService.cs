using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace FootballFormation.Core.Services;

/// <see cref="ValidateCredentialsAsync"/> deliberately breaks the Result convention the rest of this class follows: a Result carrying a
/// message would let an attacker tell "wrong password" from "no such user".
public class UserService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ICurrentTeam currentTeam,
    ILogger<UserService> logger)
{
    public const int MinPasswordLength = 8;

    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly AppUser DummyUser = new();
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, "dummy-password-for-timing");

    public async Task<AppUser?> ValidateCredentialsAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await VerifyAsync(db, username, password, cancellationToken);
    }

    /// Null when the session the principal stands for is no longer good. Shared by OnValidatePrincipal and the circuit's revalidation
    /// loop so the two cannot drift on the answer.
    public Task<AppUser?> FindForSessionAsync(
        ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        var stamp = principal?.FindFirst(AppClaims.SecurityStamp)?.Value;
        var userId = principal?.FindFirst(AppClaims.UserId)?.Value;

        // Cookies issued before the security stamp shipped carry neither claim. Rejected rather than trusted; the cost is one sign-in.
        return stamp is not null && int.TryParse(userId, out var id)
            ? FindForSessionAsync(id, stamp, cancellationToken)
            : Task.FromResult<AppUser?>(null);
    }

    /// Null once the account is deleted or its authority changed. On every authenticated request, so it stays no-tracking and by key.
    public async Task<AppUser?> FindForSessionAsync(
        int userId, string securityStamp, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user?.SecurityStamp == securityStamp ? user : null;
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        string username, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
            return PasswordChangeResult.PasswordTooShort;

        if (newPassword == currentPassword)
            return PasswordChangeResult.PasswordReused;

        // One context for both the check and the write: the user has to stay tracked, or the new hash lands on an entity nothing saves.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var user = await VerifyAsync(db, username, currentPassword, cancellationToken);
        if (user is null) return PasswordChangeResult.InvalidCurrentPassword;

        user.PasswordHash = Hasher.HashPassword(user, newPassword);
        user.SecurityStamp = NewStamp();

        // The new stamp above signs the gated cookie out, so the next request re-reads MustChangePassword rather than trusting the
        // claim minted at login.
        user.MustChangePassword = false;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Password changed for user {Username}", username);
        return PasswordChangeResult.Success;
    }

    /// Projected to <see cref="UserSummary"/> so the hash and stamp never leave here, and ordered by name — the column the list is read
    /// down. Everyone for an application admin; otherwise the accounts on the team in scope.
    ///
    /// Guarded rather than public, unlike the squad and the fixtures: this is names and logins, and anyone can point the ff.team cookie
    /// at any team, so the read has to ask the same question the writes do rather than trust where the cookie points.
    public Task<Result<List<UserSummary>>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "load users", cancellationToken, async () =>
        {
            var everyTeam = await currentUser.IsApplicationAdminAsync();
            var teamId = everyTeam ? null : await currentTeam.GetIdAsync();

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var users = await db.Users
                .AsNoTracking()
                .Where(u => everyTeam || (u.TeamId != null && u.TeamId == teamId))
                .OrderBy(u => u.DisplayName)
                .Select(u => new UserSummary(u.Id, u.DisplayName, u.Username, u.Role, u.TeamId))
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} users", users.Count);
            return Result.Success(users);
        });

    /// The account /dev/login signs in as — the seeded "admin" if present, otherwise the lowest-numbered admin, so adding a user never
    /// changes who it picks. Hands back the whole entity, not the list projection, because the principal it mints carries the stamp.
    public async Task<AppUser?> FindDevLoginAdminAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var admins = await db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Admin || u.Role == UserRole.ApplicationAdmin)
            .ToListAsync(cancellationToken);

        return admins.FirstOrDefault(u => u.Username == "admin") ?? admins.OrderBy(u => u.Id).FirstOrDefault();
    }

    /// A null <paramref name="teamId"/> means the team in scope — the one /users is looking at, and the only one an ordinary admin
    /// could name anyway.
    public Task<Result<AppUser>> CreateAsync(
        string displayName, string username, string password, UserRole role, int? teamId = null,
        CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create the user", cancellationToken, async () =>
        {
            var validation = ValidateFields(displayName, username);
            if (validation.IsFailure) return validation.To<AppUser>();

            var authority = await MayChangeAsync(role);
            if (authority.IsFailure) return authority.To<AppUser>();

            teamId = role == UserRole.ApplicationAdmin ? null : teamId ?? await currentTeam.GetIdAsync();

            var manage = await MayManageAsync(teamId);
            if (manage.IsFailure) return manage.To<AppUser>();

            if (password.Length < MinPasswordLength)
                return Result.Failure<AppUser>(PasswordTooShortKey, MinPasswordLength);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var team = await ValidateTeamAsync(db, role, teamId, cancellationToken);
            if (team.IsFailure) return team.To<AppUser>();

            username = username.Trim();
            if (await db.Users.AnyAsync(u => u.Username == username, cancellationToken))
                return Result.Failure<AppUser>(DuplicateLoginKey, username);

            var user = new AppUser
            {
                DisplayName = displayName.Trim(),
                Username = username,
                Role = role,
                TeamId = teamId,
                SecurityStamp = NewStamp()
            };
            user.PasswordHash = Hasher.HashPassword(user, password);

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created user {Username} ({UserId}) with role {Role} on team {TeamId}",
                user.Username, user.Id, role, teamId);
            return Result.Success(user);
        });

    /// No password here on purpose: changing one has to invalidate sessions, so it is its own action (<see cref="SetPasswordAsync"/>).
    /// A null <paramref name="teamId"/> leaves the account on the team it is already on.
    public Task<Result> UpdateAsync(
        int id, string displayName, string username, UserRole role, int? teamId = null,
        CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update the user", cancellationToken, async () =>
        {
            var validation = ValidateFields(displayName, username);
            if (validation.IsFailure) return validation;

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null) return NotFound(id);

            teamId = role == UserRole.ApplicationAdmin ? null : teamId ?? user.TeamId;

            // Both sides of both moves: taking a role or a team away is the same authority as handing it out.
            if (user.Role != role)
            {
                var authority = await MayChangeAsync(user.Role, role);
                if (authority.IsFailure) return authority;
            }

            var manage = await MayManageAsync(user.TeamId, teamId);
            if (manage.IsFailure) return manage;

            var team = await ValidateTeamAsync(db, role, teamId, cancellationToken);
            if (team.IsFailure) return team;

            username = username.Trim();
            if (await db.Users.AnyAsync(u => u.Username == username && u.Id != id, cancellationToken))
                return Result.Failure(DuplicateLoginKey, username);

            // Promoting the account out of the role and handing it to another team leave this one as unrun as deleting it would.
            if (user.Role == UserRole.Admin && (role != UserRole.Admin || teamId != user.TeamId)
                && await IsLastAdminOfTeamAsync(db, id, user.TeamId, cancellationToken))
                return await LastTeamAdminFailureAsync(db, user.TeamId, cancellationToken);

            if (user.Role == UserRole.ApplicationAdmin && role != UserRole.ApplicationAdmin
                && await IsLastApplicationAdminAsync(db, id, cancellationToken))
                return Result.Failure(LastApplicationAdminKey);

            user.DisplayName = displayName.Trim();
            user.Username = username;

            // Only when the authority actually changed: a rename should not sign the user out. The team is part of that authority now,
            // so a move between teams rolls the stamp as surely as a role change does.
            if (user.Role != role || user.TeamId != teamId)
            {
                user.Role = role;
                user.TeamId = teamId;
                user.SecurityStamp = NewStamp();
            }

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated user {Username} ({UserId}) to role {Role} on team {TeamId}",
                user.Username, id, role, teamId);
            return Result.Success();
        });

    /// An admin resetting someone else's password, without knowing the old one.
    public Task<Result> SetPasswordAsync(
        int id, string newPassword, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "set the password", cancellationToken, async () =>
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
                return Result.Failure(PasswordTooShortKey, MinPasswordLength);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null) return NotFound(id);

            // Handing out a password is handing out the account, so it asks the same question deleting one does.
            var manage = await MayManageAsync(user.TeamId);
            if (manage.IsFailure) return manage;

            user.PasswordHash = Hasher.HashPassword(user, newPassword);
            user.SecurityStamp = NewStamp();
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Password reset for user {Username} ({UserId})", user.Username, id);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "delete the user", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null) return NotFound(id);

            // Deleting an application admin revokes the role as surely as demoting one does.
            var authority = await MayChangeAsync(user.Role);
            if (authority.IsFailure) return authority;

            var manage = await MayManageAsync(user.TeamId);
            if (manage.IsFailure) return manage;

            if (user.Role == UserRole.Admin && await IsLastAdminOfTeamAsync(db, id, user.TeamId, cancellationToken))
                return await LastTeamAdminFailureAsync(db, user.TeamId, cancellationToken);

            if (user.Role == UserRole.ApplicationAdmin && await IsLastApplicationAdminAsync(db, id, cancellationToken))
                return Result.Failure(LastApplicationAdminKey);

            db.Users.Remove(user);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted user {Username} ({UserId})", user.Username, id);
            return Result.Success();
        });

    /// Runs every startup and does nothing once any account exists, so a changed password is never overwritten. The seeded credentials
    /// are public knowledge, hence <see cref="AppUser.MustChangePassword"/>: it can sign in and do nothing else until that is replaced.
    public async Task EnsureAdminSeededAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken)) return;

        var admin = new AppUser
        {
            DisplayName = "Administrator",
            Username = "admin",
            Role = UserRole.ApplicationAdmin,
            SecurityStamp = NewStamp(),
            MustChangePassword = true
        };
        admin.PasswordHash = Hasher.HashPassword(admin, "admin");
        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Default admin account created (username: admin, password: admin). " +
                          "It cannot do anything until the password is changed on /settings.");
    }

    /// Takes a context the caller owns and returns the user still tracked on it, so a caller that needs to write can.
    private static async Task<AppUser?> VerifyAsync(
        AppDbContext db, string username, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        // Verify a hash even when there is no such user, so response time cannot be used to enumerate accounts.
        if (user is null)
        {
            Hasher.VerifyHashedPassword(DummyUser, DummyHash, password);
            return null;
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await db.SaveChangesAsync(cancellationToken);
        }

        return user;
    }

    /// Compares with <see cref="UserRole.Admin"/> rather than asking <see cref="UserRoleExtensions.GrantsAdmin"/>, against the usual
    /// rule: an application admin may change every team but runs none, so counting one here would leave a team with nobody running it.
    private static Task<bool> IsLastAdminOfTeamAsync(
        AppDbContext db, int excludingId, int? teamId, CancellationToken cancellationToken) =>
        db.Users.AllAsync(
            u => u.Id == excludingId || u.TeamId != teamId || u.Role != UserRole.Admin,
            cancellationToken);

    private static async Task<Result> LastTeamAdminFailureAsync(
        AppDbContext db, int? teamId, CancellationToken cancellationToken)
    {
        var team = await db.Teams
            .Include(t => t.Club)
            .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);

        return Result.Failure(LastTeamAdminKey, team?.FullName ?? string.Empty);
    }

    private static Task<bool> IsLastApplicationAdminAsync(
        AppDbContext db, int excludingId, CancellationToken cancellationToken) =>
        db.Users.AllAsync(u => u.Id == excludingId || u.Role != UserRole.ApplicationAdmin, cancellationToken);

    /// RunAdminAsync only asks about the team in scope, and an account may be on another one — so every team an account is on, before
    /// and after the change, is passed through here. An application admin answers true to all of them.
    private async Task<Result> MayManageAsync(params int?[] teamIds)
    {
        foreach (var teamId in teamIds)
        {
            if (!await currentUser.IsAdminOfAsync(teamId)) return Result.Failure(NotThisTeamKey);
        }

        return Result.Success();
    }

    private async Task<Result> ValidateTeamAsync(
        AppDbContext db, UserRole role, int? teamId, CancellationToken cancellationToken)
    {
        if (role == UserRole.ApplicationAdmin) return Result.Success();

        if (teamId is null)
        {
            logger.LogWarning("Rejected an account with role {Role} and no team", role);
            return Result.Failure(TeamRequiredKey);
        }

        return await db.Teams.AnyAsync(t => t.Id == teamId, cancellationToken)
            ? Result.Success()
            : Result.Failure("Team with ID {0} not found", teamId);
    }

    /// An ordinary admin passes RunAdminAsync, so /users would otherwise be a way to promote yourself — or to strip an application
    /// admin of a role you could not hand back. Every role entering or leaving an account is passed through here.
    private async Task<Result> MayChangeAsync(params UserRole[] roles)
    {
        if (!roles.Contains(UserRole.ApplicationAdmin)) return Result.Success();

        return await currentUser.IsApplicationAdminAsync()
            ? Result.Success()
            : Result.Failure(NotApplicationAdminKey);
    }

    private static Result ValidateFields(string displayName, string username)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return Result.Failure("Name is required");
        if (string.IsNullOrWhiteSpace(username)) return Result.Failure("Username is required");
        if (displayName.Trim().Length > 100) return Result.Failure("Name must be at most {0} characters", 100);
        if (username.Trim().Length > 50) return Result.Failure("Username must be at most {0} characters", 50);
        return Result.Success();
    }

    private static Result NotFound(int id) => Result.Failure("User with ID {0} not found", id);

    private static string NewStamp() => Guid.NewGuid().ToString("N");

    private const string DuplicateLoginKey = "A user with username {0} already exists";
    private const string LastTeamAdminKey = "{0} would be left without an administrator";
    private const string LastApplicationAdminKey = "The last application administrator cannot be removed or demoted";
    private const string NotApplicationAdminKey = "Only an application administrator can grant that role";
    private const string NotThisTeamKey = "You can only manage accounts for your own team";
    private const string TeamRequiredKey = "An administrator needs a team";
    private const string PasswordTooShortKey = "Password must be at least {0} characters";

    public enum PasswordChangeResult
    {
        Success,
        InvalidCurrentPassword,
        PasswordTooShort,
        PasswordReused
    }
}

using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

/// <summary>
/// The accounts that can sign in, and the credential check itself.
/// <para>
/// The read/write methods follow the app's <see cref="Result"/> + <see cref="ServiceOperation"/>
/// convention. <see cref="ValidateCredentialsAsync"/> deliberately does not: the login endpoint
/// wants "these credentials are wrong" to be indistinguishable from "no such user", and a
/// <c>Result</c> carrying a message would give an attacker something to tell them apart with.
/// </para>
/// </summary>
public class UserService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ILogger<UserService> logger)
{
    public const int MinPasswordLength = 8;

    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly AppUser DummyUser = new();
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, "dummy-password-for-timing");

    // ---------------------------------------------------------------- authentication

    public async Task<AppUser?> ValidateCredentialsAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await VerifyAsync(db, username, password, cancellationToken);
    }

    /// <summary>
    /// The account behind a live cookie, or null when it has been deleted or its authority changed
    /// since the cookie was issued. Called on every authenticated request — see OnValidatePrincipal
    /// in Program.cs — so it reads no-tracking and touches one row by key.
    /// </summary>
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

        // One context for both the check and the write: the user has to stay tracked between them,
        // or the new hash is set on an entity nothing is going to save.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var user = await VerifyAsync(db, username, currentPassword, cancellationToken);
        if (user is null) return PasswordChangeResult.InvalidCurrentPassword;

        user.PasswordHash = Hasher.HashPassword(user, newPassword);
        user.SecurityStamp = NewStamp();

        // The seeded account is only held back until its owner picks a password of their own —
        // this is the moment that happens. The new stamp above signs the gated cookie out, so the
        // next request re-reads the flag rather than trusting the claim minted at login.
        user.MustChangePassword = false;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Password changed for user {Username}", username);
        return PasswordChangeResult.Success;
    }

    // ---------------------------------------------------------------- management

    /// <summary>By name, because that is the column the user list is read down.</summary>
    public Task<Result<List<AppUser>>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "load users", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var users = await db.Users
                .AsNoTracking()
                .OrderBy(u => u.DisplayName)
                .ToListAsync(cancellationToken);

            logger.LogDebug("Retrieved {Count} users", users.Count);
            return Result.Success(users);
        });

    public Task<Result<AppUser>> CreateAsync(
        string displayName, string username, string password, UserRole role,
        CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create the user", cancellationToken, async () =>
        {
            var validation = ValidateFields(displayName, username);
            if (validation.IsFailure) return validation.To<AppUser>();

            if (password.Length < MinPasswordLength)
                return Result.Failure<AppUser>(PasswordTooShortKey, MinPasswordLength);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            username = username.Trim();
            if (await db.Users.AnyAsync(u => u.Username == username, cancellationToken))
                return Result.Failure<AppUser>(DuplicateLoginKey, username);

            var user = new AppUser
            {
                DisplayName = displayName.Trim(),
                Username = username,
                Role = role,
                SecurityStamp = NewStamp()
            };
            user.PasswordHash = Hasher.HashPassword(user, password);

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created user {Username} ({UserId}) with role {Role}", user.Username, user.Id, role);
            return Result.Success(user);
        });

    /// <summary>
    /// Name, login and role. The password is deliberately not here — changing it has to invalidate
    /// sessions, so it is its own action (<see cref="SetPasswordAsync"/>).
    /// </summary>
    public Task<Result> UpdateAsync(
        int id, string displayName, string username, UserRole role,
        CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "update the user", cancellationToken, async () =>
        {
            var validation = ValidateFields(displayName, username);
            if (validation.IsFailure) return validation;

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null) return NotFound(id);

            username = username.Trim();
            if (await db.Users.AnyAsync(u => u.Username == username && u.Id != id, cancellationToken))
                return Result.Failure(DuplicateLoginKey, username);

            // Demoting the last admin locks everyone out of the pages that create users, with no
            // way back in short of editing the database by hand.
            if (user.Role == UserRole.Admin && role != UserRole.Admin && await IsLastAdminAsync(db, id, cancellationToken))
                return Result.Failure(LastAdminKey);

            user.DisplayName = displayName.Trim();
            user.Username = username;

            // Only when the authority actually changed: a rename should not sign the user out.
            if (user.Role != role)
            {
                user.Role = role;
                user.SecurityStamp = NewStamp();
            }

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Updated user {Username} ({UserId}) to role {Role}", user.Username, id, role);
            return Result.Success();
        });

    /// <summary>An admin resetting someone else's password, without knowing the old one.</summary>
    public Task<Result> SetPasswordAsync(
        int id, string newPassword, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "set the password", cancellationToken, async () =>
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
                return Result.Failure(PasswordTooShortKey, MinPasswordLength);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null) return NotFound(id);

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

            if (user.Role == UserRole.Admin && await IsLastAdminAsync(db, id, cancellationToken))
                return Result.Failure(LastAdminKey);

            db.Users.Remove(user);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Deleted user {Username} ({UserId})", user.Username, id);
            return Result.Success();
        });

    /// <summary>
    /// Gives a fresh install someone to sign in as. Runs on every startup and does nothing once any
    /// account exists, so a changed password is never overwritten.
    /// <para>
    /// The credentials are public knowledge, which on a reachable deployment is a handed-over key.
    /// So the account is seeded with <see cref="AppUser.MustChangePassword"/> set: it can sign in,
    /// and it can do nothing else until the password is replaced. That keeps a fresh clone usable
    /// without leaving a working admin login on the internet.
    /// </para>
    /// </summary>
    public async Task EnsureAdminSeededAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken)) return;

        var admin = new AppUser
        {
            DisplayName = "Administrator",
            Username = "admin",
            Role = UserRole.Admin,
            SecurityStamp = NewStamp(),
            MustChangePassword = true
        };
        admin.PasswordHash = Hasher.HashPassword(admin, "admin");
        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Default admin account created (username: admin, password: admin). " +
                          "It cannot do anything until the password is changed on /settings.");
    }

    // ---------------------------------------------------------------- internals

    /// <summary>
    /// The credential check itself, on a context the caller owns. Returns the tracked user so a
    /// caller that needs to write to it can, and null when the credentials do not match.
    /// </summary>
    private static async Task<AppUser?> VerifyAsync(
        AppDbContext db, string username, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        // Always run a hash verification to keep timing constant whether or not
        // the username exists, mitigating user-enumeration via response time.
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

    private static Task<bool> IsLastAdminAsync(
        AppDbContext db, int excludingId, CancellationToken cancellationToken) =>
        db.Users.AllAsync(u => u.Id == excludingId || u.Role != UserRole.Admin, cancellationToken);

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
    private const string LastAdminKey = "The last administrator cannot be removed or demoted";
    private const string PasswordTooShortKey = "Password must be at least {0} characters";

    public enum PasswordChangeResult
    {
        Success,
        InvalidCurrentPassword,
        PasswordTooShort,
        PasswordReused
    }
}

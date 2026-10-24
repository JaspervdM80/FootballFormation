using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace FootballFormation.Core.Services;

/// The links that let someone choose their own password: an invite or reset an admin copies from /users, and a reset mailed on request.
/// The request and the reset are anonymous writes — nobody who forgot their password can sign in to ask — so the host rate-limits both.
public class PasswordLinkService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    EmailProtector emailProtector,
    TimeProvider time,
    ILogger<PasswordLinkService> logger)
{
    /// Long enough for a coach to get round to a link passed on in a chat.
    public static readonly TimeSpan CopiedLinkLifetime = TimeSpan.FromDays(7);

    public static readonly TimeSpan MailedLinkLifetime = TimeSpan.FromHours(1);

    /// One mail per account per window, however often the form is sent, so the form cannot be used to flood someone's inbox.
    public static readonly TimeSpan MailInterval = TimeSpan.FromMinutes(5);

    /// Returns the token itself, which is never stored: the caller turns it into a link, and this is the only time it exists.
    public Task<Result<string>> CreateLinkAsync(int userId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "create the sign-in link", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null) return Result.Failure<string>("User with ID {0} not found", userId);

            // Choosing an account's password is taking it over, so this asks what SetPasswordAsync asks.
            if (!await currentUser.IsAdminOfAsync(user.TeamId))
                return Result.Failure<string>("You can only manage accounts for your own team");

            var token = await IssueAsync(db, user, CopiedLinkLifetime, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Created a sign-in link for user {Username} ({UserId})", user.Username, user.Id);
            return Result.Success(token);
        });

    /// Null when no mail should go out — no account uses the address, or one was sent within <see cref="MailInterval"/> — and the caller
    /// must answer exactly as it does when one should, or the form tells a stranger which addresses have an account.
    public Task<Result<PasswordResetMail?>> RequestResetAsync(string email, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync<PasswordResetMail?>(logger, "request a password reset", cancellationToken, async () =>
        {
            if (EmailProtector.Normalise(email) is not { } normalised || !EmailProtector.IsValid(normalised))
                return Result.Success<PasswordResetMail?>(null);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var user = await emailProtector.FindOwnerAsync(db.Users, normalised, cancellationToken);
            if (user is null)
            {
                logger.LogInformation("A password reset was requested for an address no account uses");
                return Result.Success<PasswordResetMail?>(null);
            }

            var now = time.GetUtcNow().UtcDateTime;
            var earlier = await db.PasswordLinks.AsNoTracking().Where(l => l.UserId == user.Id).ToListAsync(cancellationToken);
            if (earlier.Any(l => now - l.CreatedUtc < MailInterval))
            {
                logger.LogWarning("Held back a password reset mail for user {UserId}: one went out minutes ago", user.Id);
                return Result.Success<PasswordResetMail?>(null);
            }

            var token = await IssueAsync(db, user, MailedLinkLifetime, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Issued a password reset link for user {UserId}", user.Id);
            return Result.Success<PasswordResetMail?>(new PasswordResetMail(normalised, user.DisplayName, user.Username, token));
        });

    public Task<Result<bool>> IsUsableAsync(string? token, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "check the sign-in link", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return Result.Success(await FindUsableAsync(db, token, cancellationToken) is not null);
        });

    /// Signs the account out everywhere and retires every link it had, this one included — a link works once.
    public Task<Result> ResetPasswordAsync(string? token, string newPassword) =>
        ServiceOperation.RunAsync(logger, "set the password", CancellationToken.None, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (await FindUsableAsync(db, token, CancellationToken.None) is not { } user)
            {
                logger.LogWarning("Refused a password link that has expired, was used, or never existed");
                return Result.Failure(UnusableLinkKey);
            }

            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < UserService.MinPasswordLength)
                return Result.Failure("Password must be at least {0} characters", UserService.MinPasswordLength);

            user.PasswordHash = UserService.Hasher.HashPassword(user, newPassword);
            user.SecurityStamp = UserService.NewStamp();
            user.MustChangePassword = false;

            db.PasswordLinks.RemoveRange(await db.PasswordLinks.Where(l => l.UserId == user.Id).ToListAsync());
            await db.SaveChangesAsync();

            logger.LogInformation("Password set through a link for user {Username} ({UserId})", user.Username, user.Id);
            return Result.Success();
        });

    public const string UnusableLinkKey = "This link has expired or has already been used";

    /// Stages the new link and drops the account's earlier ones, so only the latest link handed out works. The caller saves.
    private async Task<string> IssueAsync(AppDbContext db, AppUser user, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        db.PasswordLinks.RemoveRange(await db.PasswordLinks.Where(l => l.UserId == user.Id).ToListAsync(cancellationToken));

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow().UtcDateTime;

        db.PasswordLinks.Add(new PasswordLink
        {
            UserId = user.Id,
            TokenHash = Hash(token),
            SecurityStamp = user.SecurityStamp,
            CreatedUtc = now,
            ExpiresUtc = now + lifetime
        });

        return token;
    }

    /// The account the token opens, tracked on <paramref name="db"/>; null for an unknown, expired or superseded one alike.
    private async Task<AppUser?> FindUsableAsync(AppDbContext db, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var hash = Hash(token);
        var link = await db.PasswordLinks.AsNoTracking().FirstOrDefaultAsync(l => l.TokenHash == hash, cancellationToken);
        if (link is null) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == link.UserId, cancellationToken);
        return user is not null && link.IsUsableAt(time.GetUtcNow().UtcDateTime, user.SecurityStamp) ? user : null;
    }

    /// Unsalted is enough: the token is 256 random bits, so there is no dictionary to precompute.
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record PasswordResetMail(string Email, string DisplayName, string Username, string Token);

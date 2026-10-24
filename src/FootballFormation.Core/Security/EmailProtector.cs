using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace FootballFormation.Core.Security;

/// Encrypts an account's email address with the app's data-protection key ring, which lives beside the database rather than in it.
public sealed class EmailProtector(IDataProtectionProvider provider, ILogger<EmailProtector> logger)
{
    public const int MaxLength = 254;

    private readonly IDataProtector _protector = provider.CreateProtector("FootballFormation.UserEmail");

    /// Trimmed and lower-cased, so the forgotten-password lookup matches however the address was typed. Null for a blank one.
    public static string? Normalise(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public static bool IsValid(string email) =>
        email.Length <= MaxLength && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;

    public string? Protect(string? email) => Normalise(email) is { } normalised ? _protector.Protect(normalised) : null;

    /// Decrypts every stored address and compares in memory: each ciphertext differs, so SQL cannot match one, and the accounts are few.
    public async Task<AppUser?> FindOwnerAsync(
        IQueryable<AppUser> users, string normalisedEmail, CancellationToken cancellationToken)
    {
        var withEmail = await users.Where(u => u.ProtectedEmail != null).ToListAsync(cancellationToken);
        return withEmail.FirstOrDefault(u => Unprotect(u.ProtectedEmail) == normalisedEmail);
    }

    /// Null, not a throw, for a ciphertext this key ring cannot open — what a live database copied to a laptop reads as.
    public string? Unprotect(string? protectedEmail)
    {
        if (protectedEmail is null) return null;

        try
        {
            return _protector.Unprotect(protectedEmail);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("An account's email could not be decrypted with this key ring");
            return null;
        }
    }
}

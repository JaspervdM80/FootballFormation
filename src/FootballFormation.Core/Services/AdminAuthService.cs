using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class AdminAuthService(AppDbContext db, ILogger<AdminAuthService> logger)
{
    public const int MinPasswordLength = 8;

    private static readonly PasswordHasher<AdminUser> Hasher = new();
    private static readonly AdminUser DummyUser = new();
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, "dummy-password-for-timing");

    public async Task<AdminUser?> ValidateCredentialsAsync(string username, string password)
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);

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
            await db.SaveChangesAsync();
        }

        return user;
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
            return PasswordChangeResult.PasswordTooShort;

        if (newPassword == currentPassword)
            return PasswordChangeResult.PasswordReused;

        var user = await ValidateCredentialsAsync(username, currentPassword);
        if (user is null) return PasswordChangeResult.InvalidCurrentPassword;

        user.PasswordHash = Hasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync();
        logger.LogInformation("Password changed for admin user {Username}", username);
        return PasswordChangeResult.Success;
    }

    public enum PasswordChangeResult
    {
        Success,
        InvalidCurrentPassword,
        PasswordTooShort,
        PasswordReused
    }

    public async Task EnsureAdminSeededAsync()
    {
        if (await db.AdminUsers.AnyAsync()) return;

        var admin = new AdminUser { Username = "admin" };
        admin.PasswordHash = Hasher.HashPassword(admin, "admin");
        db.AdminUsers.Add(admin);
        await db.SaveChangesAsync();
        logger.LogWarning("Default admin account created (username: admin, password: admin). Change this immediately!");
    }
}

using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FootballFormation.Core.Services;

public class AdminAuthService(AppDbContext db, ILogger<AdminAuthService> logger)
{
    private static readonly PasswordHasher<AdminUser> Hasher = new();

    public async Task<AdminUser?> ValidateCredentialsAsync(string username, string password)
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null) return null;

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await db.SaveChangesAsync();
        }

        return user;
    }

    public async Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        var user = await ValidateCredentialsAsync(username, currentPassword);
        if (user is null) return false;

        user.PasswordHash = Hasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync();
        logger.LogInformation("Password changed for admin user {Username}", username);
        return true;
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

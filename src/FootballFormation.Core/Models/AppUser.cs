namespace FootballFormation.Core.Models;

/// A row here grants nothing on its own — what an account may do comes from <see cref="Role"/>.
public class AppUser
{
    public int Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// Unique case-sensitively — see AppUserConfiguration.
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Admin;

    /// Changes whenever the account's authority does. Copied into the cookie at login and re-checked per request, so a cookie minted
    /// before the change stops working rather than staying valid for the rest of its fortnight.
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// Set on the seeded account, whose password is public knowledge: while true the person can sign in and do nothing else, because
    /// every route sends them to /settings. Cleared by <see cref="Services.UserService.ChangePasswordAsync"/>.
    public bool MustChangePassword { get; set; }
}

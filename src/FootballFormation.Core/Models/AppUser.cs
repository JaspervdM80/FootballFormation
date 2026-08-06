namespace FootballFormation.Core.Models;

/// <summary>
/// An account that can sign in. What it may do once signed in comes from <see cref="Role"/> —
/// merely having a row here grants nothing on its own.
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>The person, as shown in the app bar and the user list.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>What they type to sign in. Unique, case-sensitively — see AppUserConfiguration.</summary>
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Admin;

    /// <summary>
    /// Changes whenever the account's authority does — password, role, or the account going away.
    /// The value is copied into the auth cookie at login and re-checked on every request
    /// (see OnValidatePrincipal in Program.cs), so a cookie minted before the change stops working
    /// instead of staying valid for the rest of its eight hours.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Set on the account a fresh install seeds, whose password is public knowledge. While it is
    /// true the app lets the person sign in but nothing else — every route sends them to /settings
    /// until they pick their own password. Cleared by
    /// <see cref="Services.UserService.ChangePasswordAsync"/>.
    /// </summary>
    public bool MustChangePassword { get; set; }
}

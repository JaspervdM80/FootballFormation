namespace FootballFormation.Core.Security;

/// [Authorize] and AuthorizeView need a compile-time constant and cannot call ToString() on an enum. Tied to <see cref="UserRole"/> by
/// nameof, so renaming a member breaks the build rather than silently unauthorizing everyone.
public static class AppRoles
{
    public const string Admin = nameof(UserRole.Admin);

    /// Whoever manages the clubs and teams the app serves, above the admin who runs one of them.
    public const string ApplicationAdmin = nameof(UserRole.ApplicationAdmin);
}

/// Claim types this app mints beyond the standard <see cref="System.Security.Claims.ClaimTypes"/> ones.
public static class AppClaims
{
    /// So a page can tell "this is me" without a name comparison.
    public const string UserId = "uid";

    /// <see cref="AppUser.DisplayName"/> — ClaimTypes.Name carries the login, not the person.
    public const string DisplayName = "display_name";

    /// <see cref="AppUser.SecurityStamp"/> as it was when the cookie was issued.
    public const string SecurityStamp = "security_stamp";

    /// <see cref="AppUser.TeamId"/>, absent on an application admin. Reassigning a team rolls the security stamp, so this claim cannot
    /// outlive the assignment it describes.
    public const string TeamId = "team_id";

    /// Changing the password rolls the security stamp, which invalidates the cookie carrying this claim — so it cannot outlive the
    /// condition it describes.
    public const string MustChangePassword = "must_change_password";
}

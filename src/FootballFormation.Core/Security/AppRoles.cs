namespace FootballFormation.Core.Security;

/// <summary>
/// The role names as they appear in the auth cookie, for the places that need a compile-time
/// constant: <c>[Authorize(Roles = AppRoles.Admin)]</c> and <c>&lt;AuthorizeView Roles="..."&gt;</c>
/// cannot call <c>ToString()</c> on an enum. Tied to <see cref="UserRole"/> by <c>nameof</c>, so
/// renaming a member breaks the build rather than silently unauthorizing everyone.
/// </summary>
public static class AppRoles
{
    public const string Admin = nameof(UserRole.Admin);
}

/// <summary>Claim types this app mints beyond the standard <see cref="System.Security.Claims.ClaimTypes"/> ones.</summary>
public static class AppClaims
{
    /// <summary>The user's <see cref="AppUser.Id"/>, so a page can tell "this is me" without a name comparison.</summary>
    public const string UserId = "uid";

    /// <summary><see cref="AppUser.DisplayName"/> — ClaimTypes.Name carries the login, not the person.</summary>
    public const string DisplayName = "display_name";

    /// <summary><see cref="AppUser.SecurityStamp"/> as it was when the cookie was issued.</summary>
    public const string SecurityStamp = "security_stamp";

    /// <summary>
    /// Present and "true" while <see cref="AppUser.MustChangePassword"/> is set. Changing the
    /// password rolls the security stamp, which invalidates the cookie carrying this claim, so it
    /// cannot outlive the condition it describes.
    /// </summary>
    public const string MustChangePassword = "must_change_password";
}

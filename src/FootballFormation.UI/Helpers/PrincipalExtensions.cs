using System.Security.Claims;
using FootballFormation.Core.Security;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// Reading the signed-in user's role and identity off a <see cref="ClaimsPrincipal"/>.
/// </summary>
public static class PrincipalExtensions
{
    public static bool IsAdmin(this ClaimsPrincipal? user) =>
        user?.IsInRole(AppRoles.Admin) == true;

    /// <summary>The person's name, falling back to their login for cookies issued before
    /// display names existed.</summary>
    public static string? DisplayName(this ClaimsPrincipal? user) =>
        user?.FindFirst(AppClaims.DisplayName)?.Value is { Length: > 0 } name
            ? name
            : user?.Identity?.Name;

    /// <summary>The signed-in account's id, or null when nobody is signed in.</summary>
    public static int? UserId(this ClaimsPrincipal? user) =>
        int.TryParse(user?.FindFirst(AppClaims.UserId)?.Value, out var id) ? id : null;

    /// <summary>
    /// True while this account is still on the password a fresh install seeded. MainLayout holds
    /// such a session on /settings until it is changed — see <see cref="AppClaims.MustChangePassword"/>.
    /// </summary>
    public static bool MustChangePassword(this ClaimsPrincipal? user) =>
        user?.FindFirst(AppClaims.MustChangePassword)?.Value == "true";
}

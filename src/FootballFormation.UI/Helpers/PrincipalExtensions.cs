using System.Security.Claims;
using FootballFormation.Core.Security;

namespace FootballFormation.UI.Helpers;

/// Reading the signed-in user's role and identity off a <see cref="ClaimsPrincipal"/>.
public static class PrincipalExtensions
{
    public static bool IsAdmin(this ClaimsPrincipal? user) =>
        user?.IsInRole(AppRoles.Admin) == true;

    public static bool IsApplicationAdmin(this ClaimsPrincipal? user) =>
        user?.IsInRole(AppRoles.ApplicationAdmin) == true;

    /// Falls back to the login for cookies issued before display names existed.
    public static string? DisplayName(this ClaimsPrincipal? user) =>
        user?.FindFirst(AppClaims.DisplayName)?.Value is { Length: > 0 } name
            ? name
            : user?.Identity?.Name;

    /// The team this account administers. Null on an application admin, who administers every team — see <see cref="TeamAuthority"/>.
    public static int? AdminTeamId(this ClaimsPrincipal? user) =>
        int.TryParse(user?.FindFirst(AppClaims.TeamId)?.Value, out var id) ? id : null;

    /// Null when nobody is signed in.
    public static int? UserId(this ClaimsPrincipal? user) =>
        int.TryParse(user?.FindFirst(AppClaims.UserId)?.Value, out var id) ? id : null;

    /// MainLayout holds such a session on /settings until the password is changed — see <see cref="AppClaims.MustChangePassword"/>.
    public static bool MustChangePassword(this ClaimsPrincipal? user) =>
        user?.FindFirst(AppClaims.MustChangePassword)?.Value == "true";
}

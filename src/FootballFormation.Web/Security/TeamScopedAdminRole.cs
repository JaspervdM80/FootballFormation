using System.Security.Claims;
using FootballFormation.Core.Security;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Authentication;

namespace FootballFormation.Web.Security;

/// The Admin role an account signed in with means admin of its own team, but every AuthorizeView and [Authorize(Roles)] reads the role
/// alone — so an admin looking at another team was shown that team's admin pages and controls. Dropped here, per request, while the team
/// in scope is not the account's, so the render tree asks the same question TeamAuthority answers for the services.
public sealed class TeamScopedAdminRole(ICurrentTeam currentTeam) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (!principal.IsAdmin() || principal.IsApplicationAdmin()) return principal;

        if (TeamAuthority.GrantsAdminOf(false, principal.AdminTeamId(), await currentTeam.GetIdAsync())) return principal;

        var identity = new ClaimsIdentity(
            principal.Claims.Where(c => !(c.Type == ClaimTypes.Role && c.Value == AppRoles.Admin)),
            principal.Identity?.AuthenticationType);

        return new ClaimsPrincipal(identity);
    }
}

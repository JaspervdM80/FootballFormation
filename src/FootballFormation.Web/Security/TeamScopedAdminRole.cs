using System.Security.Claims;
using FootballFormation.Core.Security;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Authentication;

namespace FootballFormation.Web.Security;

/// Drops the Admin role while the team in scope is not the account's own, so AuthorizeView and [Authorize(Roles)] agree with TeamAuthority.
public sealed class TeamScopedAdminRole(ICurrentTeam currentTeam) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) =>
        For(principal, await currentTeam.GetIdAsync());

    /// Either way round: a circuit hands back the role a request on another team took away. See RevalidatingUserAuthenticationStateProvider.
    public static ClaimsPrincipal For(ClaimsPrincipal principal, int? teamId)
    {
        var isAdmin = principal.IsAdmin();
        if (!isAdmin && !principal.HasClaim(AppClaims.AdminElsewhere, "true")) return principal;
        if (principal.IsApplicationAdmin()) return principal;

        var grants = TeamAuthority.GrantsAdminOf(false, principal.AdminTeamId(), teamId);
        if (grants == isAdmin) return principal;

        var claims = principal.Claims
            .Where(c => !(c.Type == ClaimTypes.Role && c.Value == AppRoles.Admin) && c.Type != AppClaims.AdminElsewhere)
            .Append(grants ? new Claim(ClaimTypes.Role, AppRoles.Admin) : new Claim(AppClaims.AdminElsewhere, "true"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, principal.Identity?.AuthenticationType));
    }
}

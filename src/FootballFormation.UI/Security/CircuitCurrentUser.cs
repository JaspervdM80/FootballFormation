using FootballFormation.Core.Security;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Security;

/// Scoped, like the provider it reads: in Blazor Server a scope is a circuit, so this answers for the tab that made the call.
public class CircuitCurrentUser(AuthenticationStateProvider authStateProvider, ICurrentTeam currentTeam) : ICurrentUser
{
    public async Task<bool> IsAdminAsync() => await IsAdminOfAsync(await currentTeam.GetIdAsync());

    /// An account still on the seeded password is deliberately not an admin yet, which is what makes the first-login gate a real
    /// restriction rather than a redirect somebody could navigate around.
    public async Task<bool> IsAdminOfAsync(int? teamId)
    {
        var user = (await authStateProvider.GetAuthenticationStateAsync()).User;
        if (!user.IsAdmin() || user.MustChangePassword()) return false;

        return TeamAuthority.GrantsAdminOf(user.IsApplicationAdmin(), user.AdminTeamId(), teamId);
    }

    public async Task<bool> IsApplicationAdminAsync()
    {
        var user = (await authStateProvider.GetAuthenticationStateAsync()).User;
        return user.IsApplicationAdmin() && !user.MustChangePassword();
    }
}

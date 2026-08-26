using FootballFormation.Core.Security;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Security;

/// <summary>
/// <see cref="ICurrentUser"/> over the signed-in principal of the current Blazor circuit.
/// <para>
/// Scoped, like the provider it reads: in Blazor Server a scope is a circuit, so this answers for
/// the tab that made the call. Registered in Program.cs.
/// </para>
/// </summary>
public class CircuitCurrentUser(AuthenticationStateProvider authStateProvider) : ICurrentUser
{
    /// <summary>
    /// An account still on the seeded password is deliberately not an admin yet. That is what makes
    /// the first-login gate an actual restriction rather than a redirect somebody could navigate
    /// around — the services refuse it too, until the password is changed.
    /// </summary>
    public async Task<bool> IsAdminAsync()
    {
        var user = (await authStateProvider.GetAuthenticationStateAsync()).User;
        return user.IsAdmin() && !user.MustChangePassword();
    }
}

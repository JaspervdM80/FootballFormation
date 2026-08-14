using FootballFormation.Core.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace FootballFormation.Web.Security;

/// <summary>
/// Re-checks a circuit's signed-in principal on a timer, and signs the circuit out when the account
/// behind it is gone or its authority has changed.
/// <para>
/// Without this a circuit's principal is whatever the HTTP request that created it carried, for as
/// long as the tab stays open — the stock provider captures it once and never asks again.
/// <c>OnValidatePrincipal</c> in Program.cs runs per *HTTP request*, and a Blazor Server tab makes
/// almost none: the whole session after the first page load is SignalR messages. So deleting an
/// account or resetting its password left the open tab working until someone reloaded it, and that
/// is not a markup problem — <c>CircuitCurrentUser</c> reads this same provider, so the write guard
/// on every service was reading the stale principal too.
/// </para>
/// <para>
/// A failed check makes the circuit anonymous; it cannot clear the cookie, because a circuit has no
/// HTTP response to set headers on. The cookie is dropped on the browser's next real request, where
/// <c>OnValidatePrincipal</c> rejects it. Between the two the user sees the anonymous app, which is
/// the point — the authority is gone the moment this notices.
/// </para>
/// </summary>
public sealed class RevalidatingUserAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    TimeSpan revalidationInterval)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => revalidationInterval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // A scope of its own rather than an injected UserService, and not for the usual DbContext
        // reason — UserService already opens its own short-lived context per call. It is that this
        // provider *is* the circuit's AuthenticationStateProvider, and UserService depends on
        // ICurrentUser, which depends on the AuthenticationStateProvider. Injecting it directly
        // closes that loop and the container refuses to build. Do not "simplify" this.
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();

        return await users.FindForSessionAsync(authenticationState.User, cancellationToken) is not null;
    }
}

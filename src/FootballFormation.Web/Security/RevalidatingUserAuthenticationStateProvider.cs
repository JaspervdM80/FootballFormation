using Microsoft.AspNetCore.Components.Server;

namespace FootballFormation.Web.Security;

/// A Blazor Server tab makes almost no HTTP requests after its first page load, so without this the stock provider would hold a deleted
/// account's principal for as long as the tab stays open — and CircuitCurrentUser reads it, so every write guard would too.
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
        // A scope of its own, not an injected UserService: this provider is the circuit's AuthenticationStateProvider, and UserService
        // depends on ICurrentUser, which depends on that — injecting it closes the loop and the container refuses to build.
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();

        return await users.FindForSessionAsync(authenticationState.User, cancellationToken) is not null;
    }
}

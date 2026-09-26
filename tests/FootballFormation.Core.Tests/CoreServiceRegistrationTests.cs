using FootballFormation.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// ServiceTestBase constructs services by hand, so only this notices a service the registration forgot or a singleton that captures the
/// scoped, team-stamped context factory — which would serve one team's data to another.
public class CoreServiceRegistrationTests : ServiceTestBase
{
    [Fact]
    public void Every_core_service_resolves_with_the_hosts_production_lifetimes()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(RawDbFactory)
            .AddScoped(_ => DbFactory)
            .AddScoped<ICurrentUser>(_ => CurrentUser)
            .AddScoped<ICurrentTeam>(_ => CurrentTeam)
            .AddFootballFormationCore();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var coreServices = services
            .Where(d => d.ServiceType.Assembly == typeof(GameService).Assembly)
            .Select(d => d.ServiceType)
            .ToList();
        foreach (var type in coreServices)
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(type));

        Assert.Contains(typeof(PlayerService), coreServices);
        Assert.Contains(typeof(PushSubscriptionService), coreServices);
        Assert.Same(provider.GetRequiredService<LiveMatchNotifier>(), scope.ServiceProvider.GetRequiredService<LiveMatchNotifier>());
    }
}

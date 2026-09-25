using FootballFormation.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// ServiceTestBase constructs services by hand, so nothing else notices a service the registration forgot or a lifetime it got wrong.
public class CoreServiceRegistrationTests : ServiceTestBase
{
    [Fact]
    public void Every_core_service_resolves_once_the_host_supplies_its_own_pieces()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(RawDbFactory)
            .AddSingleton(DbFactory)
            .AddSingleton<ICurrentUser>(CurrentUser)
            .AddSingleton<ICurrentTeam>(CurrentTeam)
            .AddFootballFormationCore();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GameService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MatchSubstitutionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<StatsService>());
        Assert.Same(provider.GetRequiredService<LiveMatchNotifier>(), scope.ServiceProvider.GetRequiredService<LiveMatchNotifier>());
    }
}

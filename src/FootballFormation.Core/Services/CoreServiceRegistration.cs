using Microsoft.Extensions.DependencyInjection;

namespace FootballFormation.Core.Services;

/// What any host needs from Core. The host still supplies the DbContext factories, ICurrentUser and ICurrentTeam, since those read its
/// own request and storage.
public static class CoreServiceRegistration
{
    public static IServiceCollection AddFootballFormationCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddMemoryCache();
        services.AddSingleton<StatsCache>();
        services.AddSingleton<StatsCacheInvalidator>();
        services.AddSingleton<MatchAudienceQuery>();
        services.AddSingleton<MatchCalendarQuery>();

        services.AddScoped<PlayerService>();
        services.AddScoped<SeasonService>();
        services.AddScoped<SeasonSquadService>();
        services.AddScoped<GameService>();
        services.AddScoped<TrainingService>();
        services.AddScoped<LiveMatchService>();
        services.AddScoped<MatchClockService>();
        services.AddScoped<MatchGoalService>();
        services.AddScoped<MatchSubstitutionService>();
        services.AddScoped<MatchPreferencesService>();
        services.AddScoped<UserService>();
        services.AddScoped<TeamService>();
        services.AddScoped<StatsService>();
        services.AddScoped<PushSubscriptionService>();

        // Shared across circuits, or a substitution on the touchline would never reach the parents watching.
        services.AddSingleton<LiveMatchNotifier>();

        return services;
    }
}

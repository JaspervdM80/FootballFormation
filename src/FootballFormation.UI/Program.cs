using FootballFormation.UI;
using FootballFormation.UI.Managers;
using FootballFormation.UI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure JSON serialization options
builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.PropertyNameCaseInsensitive = true;
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddScoped(sp => 
{
    var httpClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    return httpClient;
});

// Register managers
builder.Services.AddScoped<ISquadManager, SquadManager>();
builder.Services.AddScoped<IPlayingTimeManager, PlayingTimeManager>();
builder.Services.AddScoped<ISubstitutionManager, SubstitutionManager>();
builder.Services.AddScoped<IFormationBuilder, FormationBuilder>();

// Register services
builder.Services.AddScoped<IFormationService, FormationService>();
builder.Services.AddScoped<IGameSetupService, GameSetupService>();

// Register new squad creation services
builder.Services.AddScoped<ISquadCreationService, SquadCreationService>();
builder.Services.AddScoped<IPlayerAvailabilityService, PlayerAvailabilityService>();

await builder.Build().RunAsync();

using FootballFormation;
using FootballFormation.Managers;
using FootballFormation.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register managers
builder.Services.AddScoped<ISquadManager, SquadManager>();
builder.Services.AddScoped<IPlayingTimeManager, PlayingTimeManager>();
builder.Services.AddScoped<ISubstitutionManager, SubstitutionManager>();
builder.Services.AddScoped<IFormationBuilder, FormationBuilder>();

// Register services
builder.Services.AddScoped<IFormationService, FormationService>();
builder.Services.AddScoped<IGameSetupService, GameSetupService>();

await builder.Build().RunAsync();
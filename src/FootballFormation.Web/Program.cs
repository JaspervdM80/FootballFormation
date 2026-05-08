using System.Security.Claims;
using FootballFormation.Core.Data;
using FootballFormation.Core.Services;
using FootballFormation.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;

var dbPath = DatabasePathHelper.GetDatabasePath();
var appDataFolder = Path.GetDirectoryName(dbPath);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(appDataFolder, "logs", "footballformation-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Football Formation application");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddMudServices();

    // Compress SignalR WebSocket traffic (render diffs, events)
    builder.Services.AddResponseCompression(opts =>
    {
        opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/octet-stream"]);
    });

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}",
            x => x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

    builder.Services.AddScoped<PlayerService>();
    builder.Services.AddScoped<GameService>();
    builder.Services.AddScoped<MatchPreferencesService>();
    builder.Services.AddScoped<AdminAuthService>();

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();

    var app = builder.Build();

    // Auto-migrate database and seed admin
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrated successfully at {DbPath}", dbPath);

        var authService = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
        await authService.EnsureAdminSeededAsync();
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseResponseCompression();
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    app.MapPost("/login", async (HttpContext context, AdminAuthService authService) =>
    {
        var form = await context.Request.ReadFormAsync();
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();

        var user = await authService.ValidateCredentialsAsync(username, password);
        if (user is null)
        {
            return Results.Redirect("/login?error=true");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Results.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }).DisableAntiforgery();

    app.Map("/logout", async (HttpContext context) =>
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    }).DisableAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(typeof(FootballFormation.UI._Imports).Assembly);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

using System.Security.Claims;
using System.Threading.RateLimiting;
using FootballFormation.Core.Data;
using FootballFormation.Core.Services;
using FootballFormation.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
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
            options.LogoutPath = "/auth/logout";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Cookie.Name = "ff.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();

    // Rate limit login attempts: 5 per minute per IP, then queue/reject
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    });

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
    app.UseRateLimiter();
    app.UseAntiforgery();

    app.MapPost("/auth/login", async (
        HttpContext context,
        AdminAuthService authService,
        ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("Auth");
        var form = await context.Request.ReadFormAsync();
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var user = await authService.ValidateCredentialsAsync(username, password);
        if (user is null)
        {
            logger.LogWarning("Failed login attempt for user '{Username}' from {Ip}", username, ip);
            return Results.Redirect("/login?error=true");
        }

        logger.LogInformation("Successful login for user '{Username}' from {Ip}", username, ip);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl : "/");
    })
    .DisableAntiforgery()
    .RequireRateLimiting("login");

    app.MapPost("/auth/logout", async (HttpContext context) =>
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

static bool IsLocalUrl(string? url)
{
    if (string.IsNullOrEmpty(url)) return false;
    if (url.StartsWith("//") || url.StartsWith("/\\")) return false;
    if (url.StartsWith('/')) return true;
    return false;
}

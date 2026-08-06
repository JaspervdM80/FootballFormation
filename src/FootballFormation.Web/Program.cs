using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using FootballFormation.Core.Data;
using Microsoft.AspNetCore.DataProtection;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using FootballFormation.Core.Services;
using FootballFormation.UI.Navigation;
using FootballFormation.UI.Security;
using FootballFormation.UI.State;
using FootballFormation.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;

// Honors APP_DATA_DIR — the persistent volume when hosted (e.g. /data on Fly.io)
var dbPath = DatabasePathHelper.GetDatabasePath();
var appDataFolder = Path.GetDirectoryName(dbPath)!;

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

    builder.Services.AddLocalization();

    // Keys on disk so antiforgery/auth cookies survive container restarts
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appDataFolder, "keys")));

    // Compress SignalR WebSocket traffic (render diffs, events)
    builder.Services.AddResponseCompression(opts =>
    {
        opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/octet-stream"]);
    });

    // A factory, not a scoped context. A Blazor Server circuit lives for as long as the tab is
    // open, so a scoped DbContext would be shared by every component on the page — and two of them
    // querying at once (the layout's season picker and the page itself) throws. Each service
    // operation now opens and disposes its own short-lived context instead.
    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}",
            x => x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

    // The match clock. Injected rather than read from DateTime.UtcNow so the live-match timing
    // logic is deterministic under test — see LiveMatchService.
    builder.Services.AddSingleton(TimeProvider.System);

    // Who the services think is calling. Scoped, so it answers for the circuit that made the call.
    // Registered before them because every write path depends on it — see ICurrentUser.
    builder.Services.AddScoped<ICurrentUser, CircuitCurrentUser>();

    builder.Services.AddScoped<PlayerService>();
    builder.Services.AddScoped<SeasonService>();
    builder.Services.AddScoped<SeasonSquadService>();
    builder.Services.AddScoped<GameService>();
    builder.Services.AddScoped<LiveMatchService>();
    builder.Services.AddScoped<MatchPreferencesService>();
    builder.Services.AddScoped<UserService>();

    // Singleton: the live match screen fans changes out to every open circuit — see LiveMatchNotifier
    builder.Services.AddSingleton<LiveMatchNotifier>();

    // Scoped, so the selected season lives for the SignalR circuit — see SeasonState
    builder.Services.AddScoped<SeasonState>();

    // Scoped for the same reason: the back button follows the trail of this tab — see NavigationTrail
    builder.Services.AddScoped<NavigationTrail>();

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

            // A cookie is good for eight hours, but the authority it carries is not: deleting an
            // account or changing its role has to take effect now, not whenever the cookie lapses.
            // Every account carries a security stamp that changes when its authority does; the
            // cookie carries the stamp as it was at sign-in, and this compares the two.
            options.Events.OnValidatePrincipal = async context =>
            {
                var principal = context.Principal;
                var stamp = principal?.FindFirst(AppClaims.SecurityStamp)?.Value;
                var userId = principal?.FindFirst(AppClaims.UserId)?.Value;

                // Cookies issued before this feature shipped carry neither claim. Reject them
                // rather than trusting them — the only cost is one extra sign-in.
                if (stamp is not null && int.TryParse(userId, out var id))
                {
                    var users = context.HttpContext.RequestServices.GetRequiredService<UserService>();
                    if (await users.FindForSessionAsync(id, stamp) is not null) return;
                }

                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            };
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

    // Auto-migrate database, seed admin, and make sure a current season exists
    using (var scope = app.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        Log.Information("Database migrated successfully at {DbPath}", dbPath);

        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        await userService.EnsureAdminSeededAsync();

        // A fresh install has no games for the migration's backfill to derive seasons from
        var seasonService = scope.ServiceProvider.GetRequiredService<SeasonService>();
        await seasonService.EnsureCurrentSeasonAsync();

        // Repairs databases written before gaps were rejected — a hole between two seasons leaves
        // every date inside it belonging to no season at all
        await seasonService.CloseSeasonGapsAsync();
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // Dutch by default; the language switcher sets the culture cookie. The
    // Accept-Language provider is removed on purpose so the default is deterministic.
    var localizationOptions = new RequestLocalizationOptions()
        .SetDefaultCulture("nl")
        .AddSupportedCultures("nl", "en")
        .AddSupportedUICultures("nl", "en");
    localizationOptions.RequestCultureProviders = localizationOptions.RequestCultureProviders
        .Where(p => p is not AcceptLanguageHeaderRequestCultureProvider)
        .ToList();
    app.UseRequestLocalization(localizationOptions);

    app.UseResponseCompression();
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.UseAntiforgery();

    app.MapPost("/auth/login", async (
        HttpContext context,
        UserService userService,
        ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("Auth");
        var form = await context.Request.ReadFormAsync();
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var user = await userService.ValidateCredentialsAsync(username, password);
        if (user is null)
        {
            logger.LogWarning("Failed login attempt for user '{Username}' from {Ip}", username, ip);
            return Results.Redirect("/login?error=true");
        }

        logger.LogInformation("Successful login for user '{Username}' ({Role}) from {Ip}", username, user.Role, ip);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            PrincipalFor(user));

        return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl : "/");
    })
    .DisableAntiforgery()
    .RequireRateLimiting("login");

    app.MapPost("/auth/logout", async (HttpContext context) =>
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    }).DisableAntiforgery();

    // Development only: signs in as an existing admin without credentials, so the [Authorize]
    // screens can be opened and inspected without anyone typing a password into the login form. It
    // mints exactly the principal /auth/login does — same claims, same real database row — so what
    // you see is the real authorized UI, and the security-stamp check accepts the cookie.
    //
    // Two independent guards, either one sufficient: the endpoint is not mapped outside
    // Development (the Fly.io container runs Production), and it refuses non-loopback callers.
    // Do NOT relax either — this is an unauthenticated route to full admin rights.
    if (app.Environment.IsDevelopment())
    {
        app.MapGet("/dev/login", async (HttpContext context, UserService userService) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !IPAddress.IsLoopback(remote))
                return Results.NotFound();

            var usersResult = await userService.GetAllAsync();
            if (usersResult.IsFailure) return Results.NotFound();

            // The seeded account by preference, otherwise the oldest admin. Deterministic on
            // purpose: GetAllAsync orders by display name, so "first admin" would otherwise mean
            // "whoever happens to sort first", and adding a user could silently change who the
            // dev route signs you in as.
            var admins = usersResult.Value!.Where(u => u.Role == UserRole.Admin).ToList();
            var admin = admins.FirstOrDefault(u => u.Username == "admin")
                ?? admins.OrderBy(u => u.Id).FirstOrDefault();
            if (admin is null) return Results.NotFound();

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                PrincipalFor(admin));

            return Results.Redirect("/");
        });

        Log.Warning("Dev login endpoint mapped at /dev/login (Development + loopback only)");
    }

    // Language switcher target: persists the choice in the culture cookie, then
    // reloads so the whole circuit restarts in the new culture.
    app.MapGet("/culture/set", (string culture, string redirectUri, HttpContext context) =>
    {
        if (culture is "nl" or "en")
        {
            context.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
        }

        return Results.LocalRedirect($"~/{redirectUri.TrimStart('/')}");
    });

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

/// <summary>
/// The signed-in identity for an account. The one place claims are built, so /auth/login and
/// /dev/login cannot drift — a claim missing from the dev principal would mean the dev route
/// exercises a different authorization path than the real one.
/// </summary>
static ClaimsPrincipal PrincipalFor(AppUser user)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Username),
        // ToString() rather than a literal: this is the string [Authorize(Roles = ...)] matches,
        // and AppRoles ties those constants back to the same enum member names.
        new(ClaimTypes.Role, user.Role.ToString()),
        new(AppClaims.UserId, user.Id.ToString()),
        new(AppClaims.DisplayName, user.DisplayName),
        new(AppClaims.SecurityStamp, user.SecurityStamp)
    };

    // Only when set, so the common case carries no extra claim at all.
    if (user.MustChangePassword)
        claims.Add(new Claim(AppClaims.MustChangePassword, "true"));

    return new ClaimsPrincipal(
        new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
}

static bool IsLocalUrl(string? url)
{
    if (string.IsNullOrEmpty(url)) return false;
    if (url.StartsWith("//") || url.StartsWith("/\\")) return false;
    if (url.StartsWith('/')) return true;
    return false;
}

using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using FootballFormation.Core.Data;
using Microsoft.AspNetCore.DataProtection;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Security;
using FootballFormation.Core.Services;
using FootballFormation.UI.Navigation;
using FootballFormation.UI.Security;
using FootballFormation.UI.State;
using FootballFormation.Web.Components;
using FootballFormation.Web.KeepAlive;
using FootballFormation.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;

// Honors APP_DATA_DIR — the persistent volume when hosted (e.g. /data on Fly.io)
var dbPath = DatabasePathHelper.GetDatabasePath();
var appDataFolder = Path.GetDirectoryName(dbPath)!;

// The commit this container was built from, baked in by the Dockerfile's GIT_SHA build arg. The
// deploy workflow compares it against the commit it just built, which is how a deploy that
// "succeeded" while the previous machine kept serving gets caught. "unknown" locally, where there
// is no build arg and nothing comparing.
var appVersion = Environment.GetEnvironmentVariable("APP_GIT_SHA") is { Length: > 0 } sha
    ? sha
    : "unknown";

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
        .AddInteractiveServerComponents(options =>
        {
            // Switching away from the app on a phone suspends the tab and kills the circuit's
            // WebSocket, and the state that circuit is holding is the whole live match screen. The
            // stock three minutes is shorter than a half-time break, so someone who put their phone
            // away came back to a rebuilt page instead of rejoining the circuit still sitting there.
            //
            // `DisconnectedCircuitMaxRetained` is deliberately left at its default of 100. Tripling
            // the window triples how long each retained circuit occupies a slot, so capping the
            // count was tempting — but a slot taken is the coach's circuit evicted, which is the
            // one this exists for, and the count stays small on its own: only an *unclean*
            // disconnect parks a circuit at all. A tab closed properly sends a disconnect beacon
            // and gives its circuit up on the spot.
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
        });

    builder.Services.AddMudServices();

    builder.Services.AddLocalization();

    // Keys on disk so antiforgery/auth cookies survive container restarts — appDataFolder is the
    // mounted volume when hosted, so a deploy reads back the key ring the last one wrote.
    //
    // The application name is pinned rather than left to default, because the default is the content
    // root path: stable at /app only because the Dockerfile says WORKDIR /app, and silently
    // different the moment that changes. Keys that are still on disk but derived for another
    // purpose string are keys that cannot open a single cookie already issued, with nothing in the
    // log to say why everyone was signed out at once.
    builder.Services.AddDataProtection()
        .SetApplicationName("FootballFormation")
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
    // StatsCacheInvalidator rides on every one of them, so any write drops the cached statistics.
    builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
        options
            .UseSqlite($"Data Source={dbPath}",
                x => x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(sp.GetRequiredService<StatsCacheInvalidator>()));

    builder.Services.AddSingleton(TimeProvider.System);

    // Singletons: a per-circuit generation would leave every other circuit reading a stale copy.
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<StatsCache>();
    builder.Services.AddSingleton<StatsCacheInvalidator>();

    builder.Services.AddScoped<ICurrentUser, CircuitCurrentUser>();

    builder.Services.AddScoped<PlayerService>();
    builder.Services.AddScoped<SeasonService>();
    builder.Services.AddScoped<SeasonSquadService>();
    builder.Services.AddScoped<GameService>();
    builder.Services.AddScoped<LiveMatchService>();
    builder.Services.AddScoped<MatchClockService>();
    builder.Services.AddScoped<MatchGoalService>();
    builder.Services.AddScoped<MatchSubstitutionService>();
    builder.Services.AddScoped<MatchPreferencesService>();
    builder.Services.AddScoped<UserService>();
    builder.Services.AddScoped<StatsService>();

    // Singleton, not scoped: a substitution on the sideline has to reach every circuit watching,
    // not just the one that made it.
    builder.Services.AddSingleton<LiveMatchNotifier>();

    builder.Services.AddScoped<SeasonState>();
    builder.Services.AddScoped<NavigationTrail>();

    // What the request knew, for the components rendered in its scope. A static render and a
    // circuit are two different scopes and each gets its own — which is the point: a circuit is
    // created *during* a request too (the /_blazor one), and that request carries the same cookies,
    // so both scopes resolve the same season without anyone asking the browser. The referrer is the
    // one thing /_blazor does not carry; see NavigationTrail for what that costs.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped(sp =>
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext is { } http
            ? new RequestContext(
                http.Request.Cookies[SeasonPreference.CookieName],
                http.Request.Headers.Referer.ToString())
            : RequestContext.None);

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/auth/logout";

            // Long enough to span the gap between one match day and the next: signing in on
            // Saturday morning should not mean typing a password again before Sunday's kickoff.
            // Sliding, so regular use through a season never lapses while an abandoned session
            // still does. This governs the ticket *inside* the cookie; how long the browser keeps
            // the cookie at all is IsPersistent's job — see PersistentSession.
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.Cookie.Name = "ff.auth";
            options.Cookie.HttpOnly = true;

            // Lax rather than Strict. Strict withholds the cookie on *every* cross-site navigation
            // including a plain link click, so arriving from WhatsApp, an email or a search result
            // rendered the page signed out and only a reload — same-site by then — put it right.
            // Lax still withholds it on the cross-site POST that CSRF needs, and no flow here is
            // reached by one.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

            // A cookie is good for a fortnight, but the authority it carries is not: deleting an
            // account or resetting its password has to take effect now, not whenever the cookie
            // lapses. The longer the cookie lives, the more this is the thing holding the line.
            // Every account carries a security stamp that changes when its authority does; the
            // cookie carries the stamp as it was at sign-in, and this compares the two.
            //
            // This runs per HTTP request, which a Blazor Server tab makes very few of — see
            // RevalidatingUserAuthenticationStateProvider for the half that covers the circuit.
            options.Events.OnValidatePrincipal = async context =>
            {
                var users = context.HttpContext.RequestServices.GetRequiredService<UserService>();
                if (await users.FindForSessionAsync(context.Principal, context.HttpContext.RequestAborted) is not null)
                    return;

                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            };
        });
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();

    // Replaces the stock ServerAuthenticationStateProvider, which reads the principal once when the
    // circuit is created and never asks again. Registered after AddInteractiveServerComponents so
    // this wins; it derives from ServerAuthenticationStateProvider, which is what lets the circuit
    // still hand it the initial state.
    //
    // Five minutes by default: one indexed read by primary key per signed-in circuit, and only for
    // signed-in ones — the loop does not start for an anonymous visitor, which is most of this
    // app's traffic. Configurable because the UI tests need it to fire inside a test's lifetime,
    // and because "how stale may authority be" is an operational question, not a constant.
    //
    // Zero leaves the stock provider in place, which is the pre-revalidation behaviour. It exists
    // so the UI test for this can be run against an app without it and actually go red — a test
    // that cannot fail is not evidence. It is not a setting to reach for in production.
    var revalidationInterval = TimeSpan.FromSeconds(
        builder.Configuration.GetValue("Auth:RevalidationIntervalSeconds", 300));

    if (revalidationInterval > TimeSpan.Zero)
        builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
            new RevalidatingUserAuthenticationStateProvider(
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                revalidationInterval));

    builder.Services.AddSingleton<KeepAliveTracker>();

    // Gated on actually running on Fly, not on !IsDevelopment(): a published build run from a
    // laptop (`dotnet publish` + the DLL, exactly what CI's browser jobs and a manual smoke test
    // do) is ASPNETCORE_ENVIRONMENT=Production too, and must not start pinging the live site every
    // two minutes with no way to notice or turn it off. FLY_APP_NAME is set by the platform itself
    // on every machine, never locally — same idea as the WEBSITE_INSTANCE_ID check in
    // DatabasePathHelper. See KeepAlivePingService for why this exists.
    if (Environment.GetEnvironmentVariable("FLY_APP_NAME") is { Length: > 0 })
    {
        builder.Services.AddHttpClient("KeepAlive", client => client.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddHostedService<KeepAlivePingService>();
    }

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

        // Snapshot first, and refuse to migrate if that fails. Migrations here are unattended and
        // some are one-way — dropping a column, deleting rows — so the copy taken in the seconds
        // before is the only route back from a bad one. A container that will not start is a bad
        // afternoon; a season of lineups quietly rewritten with no snapshot is not recoverable at
        // all, and that is the trade this makes.
        var dbLogger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseSafety");

        var backupPath = await DatabaseSafety.BackupBeforeMigrationsAsync(db, dbPath, dbLogger);

        await db.Database.MigrateAsync();
        Log.Information("Database migrated successfully at {DbPath}", dbPath);

        await DatabaseSafety.VerifyIntegrityAsync(db, dbLogger);

        if (backupPath is not null)
            Log.Information("Pre-migration backup retained at {BackupPath}", backupPath);

        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        await userService.EnsureAdminSeededAsync();

        // A fresh install has no games for the migration's backfill to derive seasons from
        var seasonService = scope.ServiceProvider.GetRequiredService<SeasonService>();
        await seasonService.EnsureCurrentSeasonAsync();

        // Repairs databases written before gaps were rejected — a hole between two seasons leaves
        // every date inside it belonging to no season at all
        await seasonService.CloseSeasonGapsAsync();
    }

    // Stamps every request as "real" activity except /health itself — a self-ping that reset its
    // own clock would keep the machine awake forever, and this endpoint's only other caller (the
    // deploy workflow's smoke check) isn't visitor activity either. See KeepAlivePingService.
    var keepAliveTracker = app.Services.GetRequiredService<KeepAliveTracker>();
    app.Use(async (context, next) =>
    {
        if (context.Request.Path != "/health")
            keepAliveTracker.Touch();

        await next();
    });

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

    // Proof that a deploy actually *serves*, not merely that the container started — which was
    // Fly's only signal before this existed, and is the one thing a bad release reliably clears.
    // It opens the database on purpose: this app migrates itself unattended on boot, so a process
    // answering 200 while SQLite is unreachable is precisely the false green that would make a
    // health check worse than none.
    //
    // It reports the commit it was built from as well, because a 200 alone cannot tell the deploy
    // whether the container answering is the one it just built — see HealthReport.
    //
    // Polled once by the deploy workflow's smoke step, and every two minutes by
    // KeepAlivePingService for as long as a real visitor was seen recently — see the middleware
    // above and docs/deployment.md. There is still deliberately no `[[http_service.checks]]` block
    // in fly.toml: a proxy-level check runs unconditionally and counts towards the concurrency
    // Fly's autostop reads, so it would hold the machine awake around the clock. The keep-alive
    // ping only runs behind KeepAliveTracker's window, and this file excludes /health from what
    // opens that window, so the ping cannot renew itself and undo scale-to-zero the same way.
    app.MapGet("/health", async (IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct) =>
    {
        HealthStatus status;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // A real query, not CanConnectAsync: for SQLite that only opens the file, which
            // succeeds against a database whose schema the migration left half-applied.
            await db.Seasons.CountAsync(ct);

            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).Count();
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
            status = HealthReport.Build(appVersion, applied, pending);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Health check failed");
            status = HealthReport.Unreachable(appVersion, ex.Message);
        }

        if (!status.IsHealthy)
            Log.Error("Health check reporting unhealthy: {Detail}", status.Detail);

        return Results.Json(status, statusCode: status.IsHealthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
    }).AllowAnonymous();

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

        var user = await userService.ValidateCredentialsAsync(username, password, context.RequestAborted);
        if (user is null)
        {
            logger.LogWarning("Failed login attempt for user '{Username}' from {Ip}", username, ip);
            return Results.Redirect("/login?error=true");
        }

        logger.LogInformation("Successful login for user '{Username}' ({Role}) from {Ip}", username, user.Role, ip);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            PrincipalFor(user),
            PersistentSession());

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

            var usersResult = await userService.GetAllAsync(context.RequestAborted);
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
                PrincipalFor(admin),
                PersistentSession());

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

    // Season picker target, the same shape as the language switcher above. A season change is a
    // navigation rather than an event because the picker lives in the layout, which renders
    // statically for every page — there is no circuit there to handle a click, and this endpoint
    // has the one thing a circuit never has: a response to put a Set-Cookie on.
    app.MapGet("/season/set", (string season, string redirectUri, HttpContext context) =>
    {
        // Anything unparseable is simply not stored. Parse already treats an absent cookie and a
        // hand-edited one the same way, so a bad query string lands the visitor back where they
        // were, on the season they already had.
        if (season == SeasonPreference.AllSeasons || int.TryParse(season, out _))
        {
            context.Response.Cookies.Append(
                SeasonPreference.CookieName,
                season,
                new CookieOptions
                {
                    // Secure is left off so this still works over the plain http:// of a local
                    // `dotnet run` — the value is a season id, not a credential. Lax because
                    // nothing cross-site needs to send it.
                    MaxAge = SeasonPreference.Lifetime,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                });
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

    // Without this the process ends successfully, and a refused boot is invisible to everything
    // outside the container: Fly sees a clean exit and the deploy that caused it reports success
    // while the site is down. The guards above — a failed backup aborting the migration, a failed
    // integrity check — are all written to stop the boot *loudly*, and this is the only part of
    // that anyone outside the log can hear.
    Environment.ExitCode = 1;
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

/// <summary>
/// What makes a sign-in outlive the browser session, and the other half of the pair with
/// <see cref="PrincipalFor"/> — both sign-in routes use both, so neither can drift.
/// <para>
/// Without <c>IsPersistent</c> the cookie goes out with no <c>Expires</c> at all, and a browser is
/// free to drop a session cookie whenever it decides the session ended. On a phone that is every
/// time the OS reclaims a backgrounded tab, and on the installed PWA every relaunch after one —
/// which is a coach putting their phone away at half time. No <c>ExpireTimeSpan</c> can rescue
/// that: it bounds the ticket the cookie carries, not the browser's willingness to keep the cookie.
/// </para>
/// <para>
/// A new instance per sign-in rather than one shared static: the cookie handler writes
/// <c>IssuedUtc</c> and <c>ExpiresUtc</c> onto the object it is handed, so a shared one would pin
/// every later sign-in to the expiry stamped on the first since boot.
/// </para>
/// </summary>
static AuthenticationProperties PersistentSession() => new() { IsPersistent = true };

static bool IsLocalUrl(string? url)
{
    if (string.IsNullOrEmpty(url)) return false;
    if (url.StartsWith("//") || url.StartsWith("/\\")) return false;
    if (url.StartsWith('/')) return true;
    return false;
}

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using FootballFormation.UI.Navigation;
using FootballFormation.UI.Security;
using FootballFormation.Web.Components;
using FootballFormation.Web.KeepAlive;
using FootballFormation.Web.Security;
using FootballFormation.Web.ServiceExtensions;
using Microsoft.AspNetCore.ResponseCompression;
using MudBlazor.Services;

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
        .AddInteractiveServerComponents(options =>
        {
            // The stock three minutes is shorter than a half-time break, so a coach who pocketed their phone came back to a rebuilt live
            // match screen. DisconnectedCircuitMaxRetained stays at its default on purpose — see docs/known_issues/touch-pwa.md.
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
        });

    builder.Services.AddMudServices();

    builder.Services.AddLocalization();

    // Keys on the mounted volume so auth cookies survive a container restart. The application name is pinned because the default is the
    // content root path, and a changed WORKDIR would silently sign everyone out at once. See docs/known_issues/authentication.md.
    builder.Services.AddDataProtection()
        .SetApplicationName("FootballFormation")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appDataFolder, "keys")));

    builder.Services.AddResponseCompression(opts =>
    {
        opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/octet-stream"]);
    });

    // A factory, not a scoped context: a circuit outlives any one query, and a shared context throws the moment the layout's season
    // picker and the page query at once. Every service operation opens its own; StatsCacheInvalidator rides along to drop stale stats.
    builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
        options
            .UseSqlite($"Data Source={dbPath}",
                x => x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(sp.GetRequiredService<StatsCacheInvalidator>()));

    builder.Services.AddSingleton(TimeProvider.System);

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

    builder.Services.AddSingleton<LiveMatchNotifier>();

    builder.Services.AddScoped<SeasonState>();
    builder.Services.AddScoped<NavigationTrail>();

    // A static render and a circuit are separate scopes, but the circuit is created during the /_blazor request, which carries the same
    // cookies — so both resolve the same season without asking the browser. The referrer is what /_blazor lacks; see NavigationTrail.
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

            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.Cookie.Name = "ff.auth";
            options.Cookie.HttpOnly = true;

            // Lax rather than Strict. Strict withholds the cookie on *every* cross-site navigation.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

            // The cookie is good for a fortnight but the authority it carries is not, so the security stamp it was signed with is
            // compared here. Per HTTP request only — RevalidatingUserAuthenticationStateProvider covers the circuit, which makes few.
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

    // Must be registered after AddInteractiveServerComponents to beat the stock provider, which reads the principal once per circuit and
    // never asks again. Zero restores that stock behaviour so the UI test for this can be made to go red; it is not a production setting.
    var revalidationInterval = TimeSpan.FromSeconds(
        builder.Configuration.GetValue("Auth:RevalidationIntervalSeconds", 300));

    if (revalidationInterval > TimeSpan.Zero)
        builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
            new RevalidatingUserAuthenticationStateProvider(
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                revalidationInterval));

    builder.Services.AddSingleton<KeepAliveTracker>();

    // FLY_APP_NAME, not !IsDevelopment(): a published build run from a laptop is Production too, and must not start pinging the live site
    // every two minutes. See KeepAlivePingService.
    if (Environment.GetEnvironmentVariable("FLY_APP_NAME") is { Length: > 0 })
    {
        builder.Services.AddHttpClient("KeepAlive", client => client.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddHostedService<KeepAlivePingService>();
    }

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

    using (var scope = app.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        // Snapshot first and refuse to migrate if that fails: these migrations run unattended and some are one-way, so a container that
        // will not start beats a season of lineups rewritten with no way back. See docs/deployment.md.
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

    // /health is excluded because a self-ping that reset its own clock would keep the machine awake forever. See KeepAlivePingService.
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

    // The Accept-Language provider is removed on purpose, so a visitor's browser cannot override the Dutch default.
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

    app.MapMinimalApi();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(typeof(FootballFormation.UI._Imports).Assembly);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");

    // Without this the process ends successfully: Fly sees a clean exit and the deploy that broke the site reports success. It is the
    // only part of a refused boot that anyone outside the container's log can hear.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

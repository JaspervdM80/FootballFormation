using System.Net;
using System.Security.Claims;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

namespace FootballFormation.Web.ServiceExtensions;

public static class Routing
{
    public static void MapMinimalApi(this WebApplication app)
    {
        // The commit this container was built from, baked in by the Dockerfile's GIT_SHA build arg. The
        // deploy workflow compares it against the commit it just built, which is how a deploy that
        // "succeeded" while the previous machine kept serving gets caught. "unknown" locally, where there
        // is no build arg and nothing comparing.
        var appVersion = Environment.GetEnvironmentVariable("APP_GIT_SHA") is { Length: > 0 } sha
            ? sha
            : "unknown";

        // Opens the database on purpose: this app migrates itself unattended on boot, so a 200 answered while SQLite is unreachable is the false green a health check exists to catch.
        // Deliberately not a fly.toml [[http_service.checks]] block — a proxy-level check runs unconditionally and would hold the machine awake past autostop. See docs/deployment.md.
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
                var admin = admins.FirstOrDefault(u => u.Username == "admin") ?? admins.OrderBy(u => u.Id).FirstOrDefault();
                if (admin is null) return Results.NotFound();

                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, PrincipalFor(admin), PersistentSession());

                return Results.Redirect("/");
            });

            Log.Warning("Dev login endpoint mapped at /dev/login (Development + loopback only)");
        }

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

        app.MapGet("/season/set", (string season, string redirectUri, HttpContext context) =>
        {
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
        new(ClaimTypes.Role, user.Role.ToString()),
        new(AppClaims.UserId, user.Id.ToString()),
        new(AppClaims.DisplayName, user.DisplayName),
        new(AppClaims.SecurityStamp, user.SecurityStamp)
    };

        // Only when set, so the common case carries no extra claim at all.
        if (user.MustChangePassword) claims.Add(new Claim(AppClaims.MustChangePassword, "true"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
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

}

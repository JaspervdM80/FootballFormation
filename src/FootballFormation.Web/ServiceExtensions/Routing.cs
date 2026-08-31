using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.UI;
using FootballFormation.UI.State;
using Microsoft.Extensions.Localization;

namespace FootballFormation.Web.ServiceExtensions;

public static class Routing
{
    public static void MapMinimalApi(this WebApplication app)
    {
        // Baked in by the Dockerfile's GIT_SHA arg and compared by the deploy workflow against the commit it just built, which is how a
        // deploy that "succeeded" while the previous machine kept serving gets caught. "unknown" locally.
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
                // A real query, not CanConnectAsync: for SQLite that only opens the file, which succeeds on a half-migrated schema.
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
            IAntiforgery antiforgery,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Auth");
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // A cross-site form cannot carry the token the sign-in page rendered, so this is what stops one logging a victim in as
            // the attacker. The endpoint reads the form itself, so nothing validates it unless we ask.
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                logger.LogWarning("Rejected a login POST with no valid antiforgery token from {Ip}", ip);
                return Results.Redirect("/login?error=true");
            }

            var form = await context.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();

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
        .RequireRateLimiting("login");

        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        // An unauthenticated route to full admin rights, held back by two independent guards: not mapped outside Development, and
        // refuses non-loopback callers. Do NOT relax either. See docs/testing/ui-testing.md.
        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/dev/login", async (HttpContext context, UserService userService) =>
            {
                var remote = context.Connection.RemoteIpAddress;
                if (remote is null || !IPAddress.IsLoopback(remote))
                    return Results.NotFound();

                var admin = await userService.FindDevLoginAdminAsync(context.RequestAborted);
                if (admin is null) return Results.NotFound();

                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, PrincipalFor(admin), PersistentSession());

                return Results.Redirect("/");
            });

            Log.Warning("Dev login endpoint mapped at /dev/login (Development + loopback only)");
        }

        // Generated rather than a file under wwwroot: the club and team are a database row now, and a static copy would be the one
        // place a rename never reached — it is the app's name on a home screen. The icons stay files, so a crest swap is a file drop.
        app.MapGet("/manifest.webmanifest", async (TeamState team, IStringLocalizer<Strings> localizer) =>
        {
            await team.EnsureLoadedAsync();
            var name = team.DisplayName;

            return Results.Json(new
            {
                name,
                short_name = name,
                description = localizer["Line-ups, squad and results for {0}.", name].Value,
                start_url = "/",
                scope = "/",
                display = "standalone",
                orientation = "portrait",
                background_color = "#ffffff",
                theme_color = "#ffffff",
                icons = new[]
                {
                    new { src = "icons/icon-192.png", sizes = "192x192", type = "image/png", purpose = "any" },
                    new { src = "icons/icon-512.png", sizes = "512x512", type = "image/png", purpose = "any" },
                    new { src = "icons/icon-512-maskable.png", sizes = "512x512", type = "image/png", purpose = "maskable" }
                }
            }, contentType: "application/manifest+json");
        }).AllowAnonymous();

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
                        // Secure is left off so this works over the plain http:// of a local `dotnet run` — a season id is not a credential.
                        MaxAge = SeasonPreference.Lifetime,
                        SameSite = SameSiteMode.Lax,
                        IsEssential = true,
                    });
            }

            return Results.LocalRedirect($"~/{redirectUri.TrimStart('/')}");
        });
    }

    /// The one place claims are built, so /auth/login and /dev/login cannot drift — a claim missing from the dev principal would mean
    /// the dev route exercises a different authorization path than the real one.
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

        // The implication UserRole records: an application admin is an admin too, and every [Authorize(Roles = Admin)] reads the claims
        // rather than the enum.
        if (user.Role == UserRole.ApplicationAdmin) claims.Add(new Claim(ClaimTypes.Role, AppRoles.Admin));

        // Only when set, so the common case carries no extra claim at all.
        if (user.MustChangePassword) claims.Add(new Claim(AppClaims.MustChangePassword, "true"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// Without IsPersistent the cookie carries no Expires, and a phone reclaiming a backgrounded tab drops it — a coach putting their
    /// phone away at half time. A new instance per sign-in, because the cookie handler stamps IssuedUtc onto the object it is handed.
    static AuthenticationProperties PersistentSession() => new() { IsPersistent = true };

    static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (url.StartsWith("//") || url.StartsWith("/\\")) return false;
        if (url.StartsWith('/')) return true;
        return false;
    }

}

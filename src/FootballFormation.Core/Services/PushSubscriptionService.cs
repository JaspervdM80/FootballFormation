using System.Buffers.Text;
using FootballFormation.Core.Security;

namespace FootballFormation.Core.Services;

/// The one write in the app that is not an admin's. Nobody signs in to follow a match, so this cannot go through
/// <see cref="ServiceOperation.RunAdminAsync{T}"/> — the endpoint in front of it is rate-limited instead, and every field is checked
/// here because all of them arrive from an anonymous caller.
public class PushSubscriptionService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentTeam currentTeam,
    TimeProvider time,
    ILogger<PushSubscriptionService> logger)
{
    private const int PublicKeyLength = 65;
    private const int AuthSecretLength = 16;

    private static readonly string[] Cultures = ["nl", "en"];

    public Task<Result> SubscribeAsync(
        string endpoint, string p256dh, string auth, string culture, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "follow this team's matches", cancellationToken, async () =>
        {
            if (!IsPushEndpoint(endpoint)) return Rejected("endpoint", endpoint);
            if (!IsKeyOfLength(p256dh, PublicKeyLength)) return Rejected("p256dh", p256dh);
            if (!IsKeyOfLength(auth, AuthSecretLength)) return Rejected("auth", auth);
            if (!Cultures.Contains(culture)) return Rejected("culture", culture);

            if (await currentTeam.GetIdAsync() is not { } teamId)
                return Result.Failure("There is no team to follow yet");

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Past the team filter: the endpoint is unique across every team, so a browser that switched teams has to be found and moved
            // rather than inserted again onto the unique index.
            var existing = await db.PushSubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

            if (existing is null)
            {
                db.PushSubscriptions.Add(new PushSubscription
                {
                    TeamId = teamId,
                    Endpoint = endpoint,
                    P256dh = p256dh,
                    Auth = auth,
                    Culture = culture,
                    CreatedAt = time.GetUtcNow().UtcDateTime
                });
            }
            else
            {
                existing.TeamId = teamId;
                existing.P256dh = p256dh;
                existing.Auth = auth;
                existing.Culture = culture;
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("A browser is now following team {TeamId}", teamId);
            return Result.Success();
        });

    /// Also how a dead endpoint is pruned — a push service answering 404 or 410 is saying this browser is gone for good.
    public Task<Result> UnsubscribeAsync(string endpoint, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "stop following this team's matches", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var removed = await db.PushSubscriptions
                .IgnoreQueryFilters()
                .Where(s => s.Endpoint == endpoint)
                .ExecuteDeleteAsync(cancellationToken);

            if (removed > 0) logger.LogInformation("A browser stopped following matches");
            return Result.Success();
        });

    /// Only ever https, and never a host of our own: a subscribe call is the one place an anonymous caller hands us a URL we later POST
    /// to, so an endpoint that is not a push service's is refused rather than turned into a request from inside the container.
    private static bool IsPushEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !uri.IsLoopback
        && uri.HostNameType is UriHostNameType.Dns
        && endpoint.Length <= 1000;

    private static bool IsKeyOfLength(string value, int length) =>
        Base64Url.IsValid(value) && Base64Url.DecodeFromChars(value).Length == length;

    private Result Rejected(string field, string value)
    {
        logger.LogWarning("Refused a push subscription with an unusable {Field} of {Length} characters", field, value.Length);
        return Result.Failure("That subscription is not usable");
    }
}

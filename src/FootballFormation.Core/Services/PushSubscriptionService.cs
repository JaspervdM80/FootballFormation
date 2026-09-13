using System.Buffers.Text;
using System.Security.Cryptography;
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
            if (!IsPublicKey(p256dh)) return Rejected("p256dh", p256dh);
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

    /// A browser rotating its endpoint has chosen nothing new, so the team and the language come off the row being replaced rather than
    /// off this request — which carries whichever team the service worker's cookies happen to name.
    public Task<Result> RenewAsync(
        string oldEndpoint, string endpoint, string p256dh, string auth, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "renew the match notifications", cancellationToken, async () =>
        {
            if (!IsPushEndpoint(endpoint)) return Rejected("endpoint", endpoint);
            if (!IsPublicKey(p256dh)) return Rejected("p256dh", p256dh);
            if (!IsKeyOfLength(auth, AuthSecretLength)) return Rejected("auth", auth);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var existing = await db.PushSubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Endpoint == oldEndpoint, cancellationToken);

            if (existing is null)
            {
                logger.LogWarning("Cannot renew a push subscription that is not on file");
                return Result.Failure("That subscription is not usable");
            }

            if (existing.Endpoint == endpoint) return Result.Success();

            // A rotation onto an endpoint already held would otherwise hit the unique index and lose the renewal entirely.
            await db.PushSubscriptions
                .IgnoreQueryFilters()
                .Where(s => s.Endpoint == endpoint)
                .ExecuteDeleteAsync(cancellationToken);

            existing.Endpoint = endpoint;
            existing.P256dh = p256dh;
            existing.Auth = auth;

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("A browser following team {TeamId} rotated its endpoint", existing.TeamId);
            return Result.Success();
        });

    /// Whether this browser follows the team in scope — the query filter is the whole check. The toggle asks on every launch, so it can
    /// never report "on" off the browser's own copy while the row behind it has been pruned.
    public Task<Result<bool>> FollowsCurrentTeamAsync(string endpoint, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "check the match notifications", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            return Result.Success(await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint, cancellationToken));
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
        !string.IsNullOrEmpty(value) && Base64Url.IsValid(value) && Base64Url.DecodeFromChars(value).Length == length;

    /// The length check is not enough: 65 arbitrary bytes pass it and then throw inside the encryption, where the failure would take
    /// down the whole match's fan-out rather than this one row. The only way to know is to import the point.
    private static bool IsPublicKey(string value)
    {
        if (!IsKeyOfLength(value, PublicKeyLength)) return false;

        var point = Base64Url.DecodeFromChars(value);
        if (point[0] != 0x04) return false;

        try
        {
            using var _ = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33], Y = point[33..PublicKeyLength] }
            });

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// A non-nullable record parameter is no guarantee: System.Text.Json binds a missing field to null regardless.
    private Result Rejected(string field, string? value)
    {
        logger.LogWarning("Refused a push subscription with an unusable {Field} of {Length} characters", field, value?.Length ?? 0);
        return Result.Failure("That subscription is not usable");
    }
}

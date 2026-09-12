using FootballFormation.Core.Push;

namespace FootballFormation.Web.Push;

/// Absent keys are an ordinary state, not a failure: a developer's machine and CI have none, and the app has to boot and serve without
/// them. Everything push-related asks <see cref="IsConfigured"/> first and quietly does nothing.
public sealed class PushConfiguration(VapidKeys? keys)
{
    public VapidKeys? Keys { get; } = keys;

    public bool IsConfigured => Keys is not null;

    /// The three come from Fly secrets in production. A key pair is generated once, by hand, and never rotated casually — the public half
    /// is what every browser stored as its applicationServerKey, so a new one silently orphans every subscription on file.
    public static PushConfiguration From(IConfiguration configuration)
    {
        var publicKey = configuration["Push:PublicKey"];
        var privateKey = configuration["Push:PrivateKey"];
        var subject = configuration["Push:Subject"];

        return publicKey is { Length: > 0 } && privateKey is { Length: > 0 } && subject is { Length: > 0 }
            ? new PushConfiguration(new VapidKeys(publicKey, privateKey, subject))
            : new PushConfiguration(null);
    }
}

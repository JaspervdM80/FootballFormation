namespace FootballFormation.Core.Models;

/// One browser that asked to hear about this team's matches. Nobody signs in to get these, so a row identifies a device and never a
/// person — there is no account to attach it to.
public class PushSubscription
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    /// The push service's URL for this browser, handed out by the browser itself. Unique across every team, which is why the upsert in
    /// PushSubscriptionService has to look it up past the team filter.
    public required string Endpoint { get; set; }

    /// The browser's public key and shared secret, base64url as the Push API hands them over. Both are needed to encrypt a payload only
    /// this browser can open.
    public required string P256dh { get; set; }

    public required string Auth { get; set; }

    /// Which language this browser subscribed in. A service worker cannot reach IStringLocalizer, so the server composes the finished
    /// text and needs to know which to use.
    public required string Culture { get; set; }

    public DateTime CreatedAt { get; set; }
}

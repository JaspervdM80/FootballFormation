using System.Net;

namespace FootballFormation.Core.Push;

public enum PushDelivery
{
    Sent,

    /// A bad minute rather than a verdict — worth another attempt.
    Retry,

    /// The push service says this browser will never be reachable again, which is the only answer that removes a subscription.
    Gone,

    /// Our own bad request. A retry would fail identically, so it is logged and left.
    Refused
}

/// Here rather than beside the sender because which answers are worth retrying is the part of push delivery most likely to be got
/// wrong, and Web carries no unit tests. A follower this is for may not open the app for weeks, so a goal dropped on a 503 is lost.
public static class PushDeliveryOutcome
{
    public static PushDelivery For(HttpStatusCode status) => status switch
    {
        >= HttpStatusCode.OK and < HttpStatusCode.Ambiguous => PushDelivery.Sent,
        HttpStatusCode.NotFound or HttpStatusCode.Gone => PushDelivery.Gone,
        HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout => PushDelivery.Retry,
        >= HttpStatusCode.InternalServerError => PushDelivery.Retry,
        _ => PushDelivery.Refused
    };
}

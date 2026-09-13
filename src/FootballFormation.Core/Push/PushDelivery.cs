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

    /// False means prune, and only <see cref="PushDelivery.Gone"/> may answer that — a run of retries that never succeeded returns true,
    /// because a push service having a bad afternoon must never unsubscribe a whole ground.
    public static async Task<bool> DeliverAsync(
        Func<Task<PushDelivery>> attempt, Func<int, Task> backOff, int maxAttempts)
    {
        for (var n = 1; ; n++)
        {
            var outcome = await attempt();

            if (outcome is not PushDelivery.Retry) return outcome is not PushDelivery.Gone;
            if (n == maxAttempts) return true;

            await backOff(n);
        }
    }
}

namespace FootballFormation.Web.KeepAlive;

/// On the hot path of every request, so the write and the read both have to be cheap and thread-safe without a lock.
public sealed class KeepAliveTracker(TimeProvider time)
{
    // Not seeded from "now": a fresh boot has no visitor yet, so starting the window open would keep every deploy awake for 30 minutes
    // with nobody there to see it.
    private long _lastActivityTicks = DateTimeOffset.MinValue.UtcTicks;

    public void Touch() => Interlocked.Exchange(ref _lastActivityTicks, time.GetUtcNow().UtcTicks);

    public bool RecentlyActive(TimeSpan window)
    {
        var lastActivity = new DateTimeOffset(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);
        return time.GetUtcNow() - lastActivity < window;
    }
}

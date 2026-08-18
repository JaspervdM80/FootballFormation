namespace FootballFormation.Web.KeepAlive;

/// <summary>
/// Tracks when the app last saw a real (non-keep-alive) request. <see cref="KeepAlivePingService"/>
/// reads this to decide whether to keep pinging the public site, so the write and the read both need
/// to be cheap and thread-safe without a lock — this is on the hot path of every request.
/// </summary>
public sealed class KeepAliveTracker(TimeProvider time)
{
    private long _lastActivityTicks = time.GetUtcNow().UtcTicks;

    public void Touch() => Interlocked.Exchange(ref _lastActivityTicks, time.GetUtcNow().UtcTicks);

    public bool RecentlyActive(TimeSpan window)
    {
        var lastActivity = new DateTimeOffset(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);
        return time.GetUtcNow() - lastActivity < window;
    }
}

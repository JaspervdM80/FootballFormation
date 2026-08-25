using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FootballFormation.Core.Services;

/// <summary>
/// Drops the cached statistics after any write, by watching the one place every write in the app
/// actually goes through.
/// <para>
/// It hangs off <c>SaveChanges</c> rather than off <see cref="ServiceOperation.RunAdminAsync"/>,
/// which is the other single choke point and the more obvious candidate. Two reasons. It is
/// *lower*: nothing reaches the database without it, so a write that somehow skipped the service
/// shape would still be caught, and there is no argument to thread through forty call sites. And
/// there is nothing left for a new write method to remember — where a required parameter can at
/// best be enforced, this cannot be forgotten, because the method that forgets it does not write.
/// </para>
/// <para>
/// Every write in this app goes through <c>SaveChangesAsync</c>: there is no <c>ExecuteUpdate</c>,
/// <c>ExecuteDelete</c> or raw SQL outside the migrations. **Adding one would go behind this
/// interceptor's back** and leave the statistics showing figures the database no longer holds.
/// </para>
/// </summary>
public sealed class StatsCacheInvalidator(StatsCache cache) : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Invalidate(result);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Invalidate(result);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Only when rows actually changed. <c>SaveChanges</c> over a context with nothing modified
    /// returns zero and is the shape a read-then-maybe-write path ends in, so bumping there would
    /// throw the cache away for a write that never happened.
    /// </summary>
    private void Invalidate(int rowsAffected)
    {
        if (rowsAffected > 0) cache.Invalidate();
    }
}

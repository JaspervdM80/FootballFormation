using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FootballFormation.Core.Services;

/// <summary>
/// Drops the cached statistics after any write.
/// <para>
/// On <c>SaveChanges</c> rather than <see cref="ServiceOperation.RunAdminAsync"/>: it sits lower,
/// needs no argument threaded through every write, and leaves nothing to remember — a new write
/// method invalidates by writing. The way past it is a write that never reaches <c>SaveChanges</c>,
/// so adding an <c>ExecuteUpdate</c>, <c>ExecuteDelete</c> or raw SQL would go behind its back.
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

    // Zero rows is a save over an unchanged context — no write to invalidate for.
    private void Invalidate(int rowsAffected)
    {
        if (rowsAffected > 0) cache.Invalidate();
    }
}

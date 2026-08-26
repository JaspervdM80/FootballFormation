using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FootballFormation.Core.Services;

// On SaveChanges rather than the service shape, so a new write method invalidates by writing. An
// ExecuteUpdate, ExecuteDelete or raw SQL would go behind its back; the app uses none outside the
// migrations.
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

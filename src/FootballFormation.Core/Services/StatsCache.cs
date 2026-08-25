using Microsoft.Extensions.Caching.Memory;

namespace FootballFormation.Core.Services;

public sealed class StatsCache(IMemoryCache cache)
{
    // Sliding: an orphaned generation goes this long after its last use.
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    private long _generation;

    public long Generation => Interlocked.Read(ref _generation);

    public void Invalidate() => Interlocked.Increment(ref _generation);

    // Take before loading and reuse for Set, so a write landing mid-rebuild orphans the result
    // instead of overwriting the live entry. See docs/patterns/service-structure.md.
    public StatsCacheKey KeyFor(string name) => new($"{name}@{Generation}");

    public bool TryGet<T>(StatsCacheKey key, out T value)
    {
        if (cache.TryGetValue(key.Value, out var found) && found is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set<T>(StatsCacheKey key, T value) =>
        cache.Set(key.Value, value, new MemoryCacheEntryOptions { SlidingExpiration = Idle });
}

// A type, not a string, so a caller cannot read one generation and store under another.
public readonly record struct StatsCacheKey(string Value);

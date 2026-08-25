using Microsoft.Extensions.Caching.Memory;

namespace FootballFormation.Core.Services;

/// <summary>
/// The built statistics reports, dropped whenever anything is written.
/// <para>
/// Nothing is invalidated — the key changes. A write bumps <see cref="Generation"/>, which is part
/// of every key, so earlier entries become unreachable and expire on their own. That also makes a
/// write landing mid-rebuild safe: <see cref="KeyFor"/> captures the generation before the caller
/// loads anything, so a report built from superseded data is stored where nobody looks. Cancelling
/// a shared eviction token instead would let that rebuild overwrite the live entry.
/// </para>
/// <para>Two concurrent misses both build — the duplicated fold is cheaper than a lock.</para>
/// </summary>
public sealed class StatsCache(IMemoryCache cache)
{
    // Sliding, so an orphaned generation goes this long after its last use.
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    private long _generation;

    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>Called from <see cref="StatsCacheInvalidator"/> after any successful save.</summary>
    public void Invalidate() => Interlocked.Increment(ref _generation);

    /// <summary>
    /// Take this before loading and pass the same one to <see cref="TryGet"/> and
    /// <see cref="Set"/>; that pairing is the safety property above, which is why it is a type.
    /// </summary>
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

/// <summary>A key with the generation folded in, so a caller cannot read one and store under another.</summary>
public readonly record struct StatsCacheKey(string Value);

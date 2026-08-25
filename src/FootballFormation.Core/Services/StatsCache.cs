using Microsoft.Extensions.Caching.Memory;

namespace FootballFormation.Core.Services;

/// <summary>
/// Holds the built statistics reports, and lets go of every one of them the moment anything is
/// written. The reports are what <c>/stats</c>, <c>/stats/positions</c> and
/// <c>/players/{id}/stats</c> spend their time on — the games load is five queries wide and the
/// per-player fold is O(games × players) — and none of those pages has a circuit to hide the wait
/// behind.
/// <para>
/// **Nothing is ever invalidated. The key changes instead.** A write bumps
/// <see cref="Generation"/>, which is part of every key, so the entries built before it are not
/// stale — they are unreachable, and expire on their own because nothing asks for them again. That
/// is what keeps this class small: there is no key registry to walk, no tag index, no eviction
/// pass, and no way for one report to be dropped while its sibling survives.
/// </para>
/// <para>
/// It is also what makes a write during a rebuild safe, which the obvious alternatives are not.
/// <see cref="KeyFor"/> captures the generation *before* the caller starts loading, and
/// <see cref="Set"/> stores under that captured key — so a report built from data a write has since
/// superseded lands where no later reader will look. Cancelling a shared eviction token instead
/// would have the in-flight rebuild write its stale result back under the live key and serve it
/// until the next write.
/// </para>
/// <para>
/// Two concurrent misses both build; there is no lock. At this app's traffic — a coach and a few
/// parents — the duplicated fold is cheaper than the machinery to prevent it, and both answers are
/// identical anyway.
/// </para>
/// </summary>
public sealed class StatsCache(IMemoryCache cache)
{
    /// <summary>
    /// How long an entry nobody asks for is kept. Sliding rather than absolute, which is what
    /// bounds the memory a bumped generation leaves behind: an orphaned entry is by definition
    /// never touched again, so it goes exactly this long after its last use, while a report that
    /// is still being read stays as long as it is wanted.
    /// </summary>
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    private long _generation;

    /// <summary>Bumped on every write. Part of every key, so bumping it orphans the lot.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>
    /// Called from <see cref="StatsCacheInvalidator"/> after any successful <c>SaveChanges</c>, so
    /// no service and no new write method has to remember to.
    /// </summary>
    public void Invalidate() => Interlocked.Increment(ref _generation);

    /// <summary>
    /// The key for a report, pinned to the generation current at this moment. Take it before
    /// loading anything and pass the same one to <see cref="TryGet"/> and <see cref="Set"/> — that
    /// pairing is the whole safety property described above, which is why this is a type and not a
    /// string.
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

/// <summary>
/// A cache key with the generation already folded in. A plain string would let a caller look up
/// one generation and store under another, which is the single thing this cache must not do.
/// </summary>
public readonly record struct StatsCacheKey(string Value);

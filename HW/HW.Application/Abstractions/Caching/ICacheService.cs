namespace HW.Application.Abstractions.Caching;

/// <summary>
/// Which tier answered a cache lookup.
/// </summary>
public enum CacheTier
{
    /// <summary>Neither tier held the key.</summary>
    Miss = 0,

    /// <summary>Served from the in-process L1 memory cache.</summary>
    Memory = 1,

    /// <summary>Served from the distributed L2 cache (Redis), and backfilled into L1.</summary>
    Distributed = 2
}

/// <summary>
/// Result of a cache lookup, carrying the tier that answered so callers can report it.
/// </summary>
public readonly record struct CacheResult<T>(T? Value, CacheTier Tier)
{
    public bool IsHit => Tier != CacheTier.Miss;

    public static CacheResult<T> Miss() => new(default, CacheTier.Miss);
}

/// <summary>
/// Two-tier cache: in-process memory (L1) in front of a distributed store (L2).
/// Implementations must degrade to L1-only when the distributed tier is unreachable — a cache
/// outage is never allowed to fail a request.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Reads <paramref name="key"/> from L1, falling back to L2 and backfilling L1 on an L2 hit.
    /// </summary>
    Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Writes <paramref name="value"/> to both tiers. L1 lifetime is capped by configuration so a
    /// missed invalidation broadcast can only go stale for a bounded window.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>Removes a single key from both tiers.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes every key under <paramref name="prefix"/> from both tiers and broadcasts the
    /// eviction so other instances drop their L1 copies too.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}

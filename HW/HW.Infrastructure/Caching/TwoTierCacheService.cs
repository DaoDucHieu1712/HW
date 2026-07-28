using System.Collections.Concurrent;
using System.Text.Json;
using HW.Application.Abstractions.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using StackExchange.Redis;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.Caching;

/// <summary>
/// L1 (in-process <see cref="IMemoryCache"/>) in front of L2 (Redis).
///
/// Reads walk L1 → L2 → miss, backfilling L1 from L2. Writes fan out to both tiers in parallel.
/// Every Redis call is guarded: if the distributed tier is unreachable the service silently
/// degrades to L1-only rather than failing the request.
///
/// L1 is per-instance, so <see cref="RemoveByPrefixAsync"/> publishes the evicted prefix on a Redis
/// pub/sub channel; every instance drops the matching L1 entries. <see cref="CacheOptions.L1MaxTtlSeconds"/>
/// is the backstop for when that broadcast cannot be delivered.
/// </summary>
public sealed class TwoTierCacheService : ICacheService, IDisposable
{
    private const string InvalidationChannel = "cache:invalidate";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IMemoryCache _l1;
    private readonly IConnectionMultiplexer? _redis;
    private readonly CacheOptions _options;
    private readonly ILogger<TwoTierCacheService> _logger;

    /// <summary>
    /// One <see cref="CancellationTokenSource"/> per key prefix. Entries written under a prefix take
    /// its token as an expiration token, so cancelling it evicts the whole prefix from L1 at once —
    /// <see cref="IMemoryCache"/> cannot otherwise be enumerated or removed by pattern.
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _prefixTokens = new();

    private ISubscriber? _subscriber;
    private int _subscribed;
    private bool _disposed;

    public TwoTierCacheService(
        IMemoryCache l1,
        IOptions<CacheOptions> options,
        ILogger<TwoTierCacheService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _l1 = l1;
        _options = options.Value;
        _logger = logger;
        _redis = redis;

        InitializeInvalidationSubscription();
    }

    public async Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (!_options.Enabled) return CacheResult<T>.Miss();

        var fullKey = Qualify(key);

        // L1 — in-process, no serialization, no network.
        if (_l1.TryGetValue(fullKey, out T? memoryHit))
            return new CacheResult<T>(memoryHit, CacheTier.Memory);

        var db = GetDatabase();
        if (db is null) return CacheResult<T>.Miss();

        try
        {
            // Single round trip for both value and remaining TTL, so the L1 backfill can inherit
            // L2's remaining lifetime instead of resetting the clock.
            var entry = await db.StringGetWithExpiryAsync(fullKey);
            if (entry.Value.IsNullOrEmpty) return CacheResult<T>.Miss();

            var value = JsonSerializer.Deserialize<T>(entry.Value!, SerializerOptions);
            if (value is null) return CacheResult<T>.Miss();

            SetL1(fullKey, key, value, entry.Expiry ?? TimeSpan.FromSeconds(_options.DefaultTtlSeconds));
            return new CacheResult<T>(value, CacheTier.Distributed);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            _logger.LogWarning(ex, "L2 cache read failed for {Key}; serving as a miss.", fullKey);
            return CacheResult<T>.Miss();
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (!_options.Enabled || value is null) return;

        var fullKey = Qualify(key);
        var lifetime = ttl ?? TimeSpan.FromSeconds(_options.DefaultTtlSeconds);

        SetL1(fullKey, key, value, lifetime);

        var db = GetDatabase();
        if (db is null) return;

        try
        {
            var payload = JsonSerializer.Serialize(value, SerializerOptions);
            await db.StringSetAsync(fullKey, payload, lifetime);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            // L1 already holds the value — the write is simply not shared with other instances.
            _logger.LogWarning(ex, "L2 cache write failed for {Key}; L1 retains the entry.", fullKey);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var fullKey = Qualify(key);
        _l1.Remove(fullKey);

        var db = GetDatabase();
        if (db is null) return;

        try
        {
            await db.KeyDeleteAsync(fullKey);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            _logger.LogWarning(ex, "L2 cache delete failed for {Key}.", fullKey);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        EvictL1Prefix(prefix);

        if (_redis is null || !_redis.IsConnected) return;

        try
        {
            // Delete L2 keys and tell peers to drop their L1 copies. Both must happen; running them
            // together keeps invalidation latency at one round trip rather than two.
            await Task.WhenAll(
                DeleteL2ByPrefixAsync(prefix),
                _subscriber?.PublishAsync(RedisChannel.Literal(Qualify(InvalidationChannel)), prefix)
                    ?? Task.FromResult(0L));
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            // L1 on this instance is already clear. Peers fall back to L1MaxTtlSeconds expiry.
            _logger.LogWarning(ex, "L2 prefix invalidation failed for {Prefix}; peers will expire within {Ttl}s.",
                prefix, _options.L1MaxTtlSeconds);
        }
    }

    private async Task DeleteL2ByPrefixAsync(string prefix)
    {
        var db = GetDatabase();
        if (db is null || _redis is null) return;

        var pattern = $"{Qualify(prefix)}:*";

        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;

            // SCAN rather than KEYS — KEYS blocks the Redis event loop for the whole keyspace.
            var batch = new List<RedisKey>(256);
            await foreach (var redisKey in server.KeysAsync(db.Database, pattern, pageSize: 256))
            {
                batch.Add(redisKey);
                if (batch.Count < 256) continue;

                await db.KeyDeleteAsync([.. batch]);
                batch.Clear();
            }

            if (batch.Count > 0) await db.KeyDeleteAsync([.. batch]);
        }
    }

    private void SetL1<T>(string fullKey, string rawKey, T value, TimeSpan ttl)
    {
        // L1 never outlives its cap: it is invisible to other instances' invalidations except via
        // the pub/sub backplane, so a short lifetime bounds the damage when that fails.
        var l1Ttl = TimeSpan.FromSeconds(Math.Min(ttl.TotalSeconds, _options.L1MaxTtlSeconds));

        var entryOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = l1Ttl };

        var prefix = ExtractPrefix(rawKey);
        if (prefix is not null)
            entryOptions.AddExpirationToken(new CancellationChangeToken(GetPrefixCts(prefix).Token));

        _l1.Set(fullKey, value, entryOptions);
    }

    private CancellationTokenSource GetPrefixCts(string prefix)
    {
        while (true)
        {
            var cts = _prefixTokens.GetOrAdd(prefix, _ => new CancellationTokenSource());

            // A concurrent eviction may have cancelled this CTS between GetOrAdd and here; binding a
            // new entry to it would evict the entry immediately. Drop it and take a fresh one.
            if (!cts.IsCancellationRequested) return cts;

            _prefixTokens.TryRemove(new KeyValuePair<string, CancellationTokenSource>(prefix, cts));
        }
    }

    private void EvictL1Prefix(string prefix)
    {
        if (_prefixTokens.TryRemove(prefix, out var cts)) cts.Cancel();
    }

    private void InitializeInvalidationSubscription()
    {
        if (_redis is null) return;

        // Subscribing against a dead connection blocks for the full connect timeout and then throws,
        // which would stall whichever request first resolves this singleton. Only subscribe once the
        // connection is actually up, and let ConnectionRestored cover the "Redis started later" case.
        _redis.ConnectionRestored += (_, _) => TrySubscribe();

        if (_redis.IsConnected) TrySubscribe();
    }

    private void TrySubscribe()
    {
        if (_redis is null || _disposed) return;

        // StackExchange.Redis re-establishes existing subscriptions itself across reconnects, so
        // this only needs to succeed once.
        if (Interlocked.Exchange(ref _subscribed, 1) == 1) return;

        try
        {
            _subscriber = _redis.GetSubscriber();
            _subscriber.Subscribe(
                RedisChannel.Literal(Qualify(InvalidationChannel)),
                (_, message) =>
                {
                    // Includes the message this instance published; evicting an already-clear prefix
                    // is a no-op, so there is no need to filter by sender.
                    if (message.HasValue) EvictL1Prefix(message.ToString());
                });
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            Interlocked.Exchange(ref _subscribed, 0); // let a later ConnectionRestored retry
            _logger.LogWarning(ex, "Could not subscribe to cache invalidation; L1 will rely on TTL expiry.");
        }
    }

    private IDatabase? GetDatabase()
        => _redis is { IsConnected: true } ? _redis.GetDatabase() : null;

    private string Qualify(string key) => $"{_options.InstanceName}{key}";

    /// <summary>
    /// Keys are <c>{prefix}:{hash}</c>; the prefix is what <see cref="RemoveByPrefixAsync"/> targets.
    /// </summary>
    private static string? ExtractPrefix(string key)
    {
        var separator = key.LastIndexOf(':');
        return separator <= 0 ? null : key[..separator];
    }

    /// <summary>
    /// Cache failures are infrastructure noise, never request failures. Programming errors
    /// (<see cref="ArgumentException"/>, <see cref="NullReferenceException"/>, …) are left to throw.
    /// </summary>
    private static bool IsCacheFailure(Exception ex)
        => ex is RedisException or RedisTimeoutException or JsonException
            or ObjectDisposedException or TimeoutException or IOException;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _subscriber?.Unsubscribe(RedisChannel.Literal(Qualify(InvalidationChannel)));
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            // Shutting down against an already-dead Redis is not worth failing dispose over.
            _logger.LogDebug(ex, "Unsubscribe during dispose failed.");
        }

        foreach (var cts in _prefixTokens.Values) cts.Dispose();
        _prefixTokens.Clear();
    }
}

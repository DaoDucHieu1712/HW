using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace HW.Api.Caching;

/// <summary>
/// Caches the response of a GET action across the two-tier cache (memory → Redis).
///
/// <code>
/// [HttpGet]
/// [Cache(120)]                                  // 120s, keyed per controller + query + user
/// public Task&lt;IActionResult&gt; GetAll([FromQuery] VocabPagingRequestDto request) { … }
/// </code>
///
/// The cache key is <c>{prefix}:{hash(path + query + user)}</c>. <see cref="Prefix"/> defaults to
/// the controller name, which is what <see cref="InvalidateCacheAttribute"/> targets.
///
/// Non-GET requests, non-200 responses, and actions that threw are never cached.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CacheAttribute : Attribute, IFilterFactory
{
    /// <param name="ttlSeconds">
    /// Lifetime in Redis. Pass 0 to use <c>Cache:DefaultTtlSeconds</c>. The memory tier is
    /// additionally capped by <c>Cache:L1MaxTtlSeconds</c>.
    /// </param>
    public CacheAttribute(int ttlSeconds = 0) => TtlSeconds = ttlSeconds;

    /// <summary>Redis lifetime in seconds; 0 defers to configuration.</summary>
    public int TtlSeconds { get; }

    /// <summary>
    /// Key namespace to store under. Defaults to the controller name (e.g. <c>vocab</c>).
    /// Set this when several controllers share an invalidation group.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Include the caller's user id in the key. Defaults to <c>true</c> so per-user data is never
    /// served across accounts. Set to <c>false</c> only for genuinely public, identical-for-everyone
    /// responses.
    /// </summary>
    public bool VaryByUser { get; set; } = true;

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        => ActivatorUtilities.CreateInstance<CacheResourceFilter>(
            serviceProvider,
            new CacheFilterOptions(TtlSeconds, Prefix, VaryByUser));
}

/// <summary>
/// Drops every cached entry under the given prefixes once the action succeeds. Put this on the
/// commands that mutate what a <see cref="CacheAttribute"/> endpoint reads.
///
/// <code>
/// [HttpPost]
/// [InvalidateCache("vocab")]
/// public Task&lt;IActionResult&gt; Create([FromBody] CreateVocabRequestDto dto) { … }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InvalidateCacheAttribute : Attribute, IFilterFactory
{
    /// <param name="prefixes">
    /// Key namespaces to clear. Omit to clear the current controller's own prefix.
    /// </param>
    public InvalidateCacheAttribute(params string[] prefixes) => Prefixes = prefixes;

    public string[] Prefixes { get; }

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        // Wrapped in an explicit object[]: passing the string[] directly would bind it to the
        // `params object[]` slot and spread each prefix into its own constructor argument.
        => ActivatorUtilities.CreateInstance<InvalidateCacheFilter>(serviceProvider, [Prefixes]);
}

/// <summary>Values captured from a <see cref="CacheAttribute"/> and handed to its filter.</summary>
public sealed record CacheFilterOptions(int TtlSeconds, string? Prefix, bool VaryByUser);

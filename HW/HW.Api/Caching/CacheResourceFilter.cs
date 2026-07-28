using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HW.Application.Abstractions.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace HW.Api.Caching;

/// <summary>A cached action response: the status code and the already-serialized body.</summary>
public sealed record CachedResponse(int StatusCode, string Body);

/// <summary>
/// Backs <see cref="CacheAttribute"/>. Runs as a resource filter so a hit short-circuits before
/// model binding and before the action (and therefore the MediatR handler and DB) is ever reached.
/// </summary>
public sealed class CacheResourceFilter : IAsyncResourceFilter
{
    private readonly ICacheService _cache;
    private readonly CacheFilterOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<CacheResourceFilter> _logger;

    public CacheResourceFilter(
        ICacheService cache,
        CacheFilterOptions options,
        IOptions<JsonOptions> jsonOptions,
        ILogger<CacheResourceFilter> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;

        // Serialize with MVC's own settings so a cached body is byte-identical to a fresh one —
        // otherwise a hit and a miss would differ in property casing.
        _jsonOptions = jsonOptions.Value.JsonSerializerOptions;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            await next();
            return;
        }

        var key = BuildKey(context);
        var ct = context.HttpContext.RequestAborted;

        var cached = await _cache.GetAsync<CachedResponse>(key, ct);
        if (cached is { IsHit: true, Value: not null })
        {
            context.HttpContext.Response.Headers["X-Cache"] = cached.Tier switch
            {
                CacheTier.Memory => "HIT-L1",
                CacheTier.Distributed => "HIT-L2",
                _ => "HIT"
            };

            context.Result = new ContentResult
            {
                Content = cached.Value.Body,
                ContentType = "application/json; charset=utf-8",
                StatusCode = cached.Value.StatusCode
            };
            return;
        }

        context.HttpContext.Response.Headers["X-Cache"] = "MISS";

        var executed = await next();

        // Never cache a failed, cancelled, or non-200 outcome.
        if (executed.Exception is not null || executed.Canceled) return;
        if (executed.Result is not ObjectResult { Value: not null } result) return;

        var statusCode = result.StatusCode ?? StatusCodes.Status200OK;
        if (statusCode is < 200 or >= 300) return;

        try
        {
            var body = JsonSerializer.Serialize(result.Value, _jsonOptions);
            var ttl = _options.TtlSeconds > 0 ? TimeSpan.FromSeconds(_options.TtlSeconds) : (TimeSpan?)null;

            await _cache.SetAsync(key, new CachedResponse(statusCode, body), ttl, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A response we cannot serialize is a cache problem, not a request problem — the caller
            // already has their result.
            _logger.LogWarning(ex, "Response for {Path} could not be cached.", context.HttpContext.Request.Path);
        }
    }

    /// <summary>
    /// Builds <c>{prefix}:{hash}</c> over path, normalized query, and (optionally) user id.
    /// The prefix is left in the clear so <see cref="ICacheService.RemoveByPrefixAsync"/> can match it.
    /// </summary>
    private string BuildKey(ResourceExecutingContext context)
    {
        var request = context.HttpContext.Request;
        var prefix = _options.Prefix ?? DefaultPrefix(context);

        var builder = new StringBuilder(request.Path.Value);

        // Sort so ?a=1&b=2 and ?b=2&a=1 resolve to one entry.
        foreach (var (name, values) in request.Query.OrderBy(q => q.Key, StringComparer.Ordinal))
            builder.Append('|').Append(name).Append('=').Append(string.Join(',', values.Select(v => v ?? string.Empty)));

        if (_options.VaryByUser)
        {
            var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? context.HttpContext.User.FindFirstValue("sub");

            if (!string.IsNullOrEmpty(userId)) builder.Append("|u=").Append(userId);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        return $"{prefix}:{hash[..32].ToLowerInvariant()}";
    }

    private static string DefaultPrefix(ResourceExecutingContext context)
        => context.ActionDescriptor is ControllerActionDescriptor descriptor
            ? descriptor.ControllerName.ToLowerInvariant()
            : "api";
}

/// <summary>
/// Backs <see cref="InvalidateCacheAttribute"/>. Clears the target prefixes only after the action
/// has genuinely succeeded.
/// </summary>
public sealed class InvalidateCacheFilter : IAsyncActionFilter
{
    private readonly ICacheService _cache;
    private readonly string[] _prefixes;

    public InvalidateCacheFilter(ICacheService cache, string[] prefixes)
    {
        _cache = cache;
        _prefixes = prefixes;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        if (executed.Exception is not null || executed.Canceled) return;

        var statusCode = executed.Result switch
        {
            ObjectResult result => result.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult result => result.StatusCode,
            _ => StatusCodes.Status200OK
        };

        if (statusCode is < 200 or >= 300) return;

        var prefixes = _prefixes.Length > 0 ? _prefixes : [DefaultPrefix(context)];

        // Fan out: each prefix is an independent SCAN + delete against Redis.
        await Task.WhenAll(prefixes.Select(prefix =>
            _cache.RemoveByPrefixAsync(prefix, context.HttpContext.RequestAborted)));
    }

    private static string DefaultPrefix(ActionExecutingContext context)
        => context.ActionDescriptor is ControllerActionDescriptor descriptor
            ? descriptor.ControllerName.ToLowerInvariant()
            : "api";
}

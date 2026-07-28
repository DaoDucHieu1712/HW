using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace HW.Infrastructure.Caching;

/// <summary>
/// Forces the <see cref="IConnectionMultiplexer"/> singleton to be built at boot rather than on the
/// first request that touches the cache.
///
/// <see cref="ConnectionMultiplexer.Connect(string, TextWriter)"/> blocks for the whole connect
/// timeout when Redis is unreachable. Because the DI factory is lazy, that stall would otherwise be
/// paid by whichever user request happens to resolve the cache first — measured at ~5s with Redis
/// down. Resolving it here on a background thread keeps it off both the startup path and the
/// request path; until it completes, <see cref="TwoTierCacheService"/> simply reports L1-only.
/// </summary>
internal sealed class RedisConnectionWarmup : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public RedisConnectionWarmup(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Deliberately not awaited: a slow or absent Redis must not delay the host from starting.
        _ = Task.Run(() =>
        {
            try
            {
                _serviceProvider.GetService<IConnectionMultiplexer>();
            }
            catch (RedisConnectionException)
            {
                // Expected when Redis is down. TwoTierCacheService degrades to L1-only and the
                // multiplexer reconnects on its own once Redis is reachable.
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

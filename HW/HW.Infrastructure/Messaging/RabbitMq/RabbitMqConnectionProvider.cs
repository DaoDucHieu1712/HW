using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Owns the single RabbitMQ connection shared by the publisher and the consumer.
///
/// The client recovers dropped connections on its own, so this only has to cover the case its
/// recovery cannot: a broker that was already unreachable when the connection was first attempted,
/// which leaves nothing to recover. Connecting lazily and re-checking <see cref="IConnection.IsOpen"/>
/// on each access means the app starts fine against a down broker and picks it up once it appears.
///
/// <para>
/// Note the contrast with the cache's Redis handling, which pre-warms its connection to keep the
/// connect stall off the request path. Nothing equivalent is needed here: publishes are already
/// expected to be awaited by callers who handle failure, and the consumer connects on a background
/// thread before any request exists.
/// </para>
/// </summary>
internal sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnectionProvider(
        IOptions<MessagingOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        _logger = logger;

        var rabbit = options.Value.RabbitMq;

        _factory = new ConnectionFactory
        {
            HostName = rabbit.HostName,
            Port = rabbit.Port,
            UserName = rabbit.UserName,
            Password = rabbit.Password,
            VirtualHost = rabbit.VirtualHost,

            // Recover the connection, its channels, and the consumers bound to them after a network
            // blip, so a broker restart does not silently leave this instance consuming nothing.
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,

            // The client dispatches our async consumer callbacks on this many threads. Left at 1
            // because the dispatcher already serializes work per message and relies on prefetch, not
            // callback concurrency, to control throughput.
            ConsumerDispatchConcurrency = 1
        };
    }

    /// <summary>
    /// Returns the open connection, establishing it on first use and re-establishing it if the
    /// client's own recovery has given up.
    /// </summary>
    /// <exception cref="RabbitMQ.Client.Exceptions.BrokerUnreachableException">
    /// The broker could not be reached. Deliberately propagated rather than swallowed: unlike a cache
    /// miss, a dropped message is data loss, so the caller decides what to do about it.
    /// </exception>
    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection is { IsOpen: true }) return _connection;

        await _gate.WaitAsync(ct);
        try
        {
            // Another caller may have reconnected while this one waited on the gate.
            if (_connection is { IsOpen: true }) return _connection;

            if (_connection is not null)
            {
                _logger.LogWarning("[RabbitMQ] Connection is closed; reconnecting.");
                await SafeDisposeAsync(_connection);
            }

            _connection = await _factory.CreateConnectionAsync(ct);

            _logger.LogInformation("[RabbitMQ] Connected to {Host}:{Port}{VHost}.",
                _factory.HostName, _factory.Port, _factory.VirtualHost);

            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SafeDisposeAsync(IAsyncDisposable resource)
    {
        try
        {
            await resource.DisposeAsync();
        }
        catch (Exception ex)
        {
            // Disposing an already-broken connection throws for the same reason it is being replaced.
            _logger.LogDebug(ex, "[RabbitMQ] Discarding a broken connection failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection is not null) await SafeDisposeAsync(_connection);

        _gate.Dispose();
    }
}

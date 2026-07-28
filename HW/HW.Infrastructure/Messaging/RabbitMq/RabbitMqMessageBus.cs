using System.Text.Json;
using HW.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Publishes to one durable topic exchange, using the topic name as the routing key.
///
/// <para>
/// <b>A message published to a topic no queue is bound to is discarded.</b> This is how AMQP
/// exchanges work and the adapter cannot paper over it — it is the sharpest behavioural difference
/// from the Kafka adapter, which retains messages whether or not a consumer exists. In practice:
/// start the consuming service at least once before the publisher, so its queue exists and
/// accumulates while it is down. A publish confirms successfully in this case; the broker accepted
/// the message and then had nowhere to route it.
/// </para>
/// </summary>
internal sealed class RabbitMqMessageBus : IBrokerBus, IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>();

    /// <summary>
    /// Publisher confirms, awaited per publish. Without these, <c>BasicPublishAsync</c> returns as
    /// soon as the bytes reach the socket, which would make <see cref="IMessageBus.PublishAsync"/>
    /// report success for messages the broker never durably accepted.
    /// </summary>
    private static readonly CreateChannelOptions ChannelOptions = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly RabbitMqConnectionProvider _connections;
    private readonly MessagingOptions _options;
    private readonly ILogger<RabbitMqMessageBus> _logger;

    /// <summary>
    /// Serializes access to <see cref="_channel"/>. An <see cref="IChannel"/> is not thread-safe, and
    /// this bus is a singleton resolved by every concurrent request.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqMessageBus(
        RabbitMqConnectionProvider connections,
        IOptions<MessagingOptions> options,
        ILogger<RabbitMqMessageBus> logger)
    {
        _connections = connections;
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : class
        => PublishAsync(message, typeof(TMessage), Guid.NewGuid().ToString(), ct);

    public Task PublishAsync(object message, Type messageType, string messageId, CancellationToken ct)
    {
        var topic = MessageSerializer.TopicOf(messageType);

        var envelope = new MessageEnvelope(
            MessageId: messageId,
            Topic: topic,
            PartitionKey: (message as IPartitionedMessage)?.PartitionKey,
            PublishedAtUtc: DateTimeOffset.UtcNow,
            Headers: NoHeaders,
            Payload: JsonSerializer.SerializeToElement(message, messageType, MessageSerializer.Options),
            PayloadType: messageType.FullName);

        return PublishEnvelopeAsync(envelope, topic, ct);
    }

    /// <summary>
    /// Publishes a ready-made envelope under an explicit routing key, preserving its id and
    /// timestamp. This is the path the consumer's dead-lettering takes: the message must arrive on
    /// <c>{topic}.dlq</c> as the same message that failed, not as a fresh one, so it can be
    /// correlated with the logs that explain why it is there.
    /// </summary>
    internal async Task PublishEnvelopeAsync(MessageEnvelope envelope, string routingKey, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var body = MessageSerializer.Serialize(envelope);

        var properties = new BasicProperties
        {
            MessageId = envelope.MessageId,
            ContentType = "application/json",
            Type = envelope.Topic,
            Timestamp = new AmqpTimestamp(envelope.PublishedAtUtc.ToUnixTimeSeconds()),

            // Survive a broker restart. Pointless without a durable exchange and queue, which is why
            // all three are set together across this adapter.
            DeliveryMode = DeliveryModes.Persistent,

            // Nothing routes on this; it rides along so Kafka's partition key is not lost when a
            // message crosses providers, and so operators can see it in the management UI.
            CorrelationId = envelope.PartitionKey
        };

        await _gate.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);

            // mandatory: false — an unrouted message is normal here (no consumer has declared its
            // queue yet) and mandatory would only surface it on a callback this adapter does not wire
            // up. See the class remarks: it would not save the message either way.
            await channel.BasicPublishAsync(
                exchange: _options.RabbitMq.Exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);
        }
        finally
        {
            _gate.Release();
        }

        _logger.LogDebug("[RabbitMQ] Published {MessageId} to {Exchange}/{RoutingKey}.",
            envelope.MessageId, _options.RabbitMq.Exchange, routingKey);
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        if (_channel is not null)
        {
            try { await _channel.DisposeAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "[RabbitMQ] Discarding a broken channel failed."); }
        }

        var connection = await _connections.GetAsync(ct);
        _channel = await connection.CreateChannelAsync(ChannelOptions, ct);

        // Idempotent, and cheap enough to repeat on every channel rebuild. Declaring it here as well
        // as in the consumer means a publish-only service still gets a working topology.
        await _channel.ExchangeDeclareAsync(
            exchange: _options.RabbitMq.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_channel is not null)
        {
            try { await _channel.DisposeAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "[RabbitMQ] Closing the publisher channel failed."); }
        }

        _gate.Dispose();
    }
}

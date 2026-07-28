using System.Text.Json;
using Confluent.Kafka;
using HW.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.Messaging.Kafka;

/// <summary>
/// Publishes to a Kafka topic per logical topic, keyed by <see cref="IPartitionedMessage.PartitionKey"/>.
///
/// <para>
/// Unlike the RabbitMQ adapter, a message published here is retained by the broker whether or not any
/// consumer exists — it sits in the log until the topic's retention expires. A consumer group that
/// starts later still sees it, subject to <c>Messaging:Kafka:AutoOffsetReset</c>.
/// </para>
/// </summary>
internal sealed class KafkaMessageBus : IBrokerBus, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>();

    private readonly IProducer<string?, byte[]> _producer;
    private readonly ILogger<KafkaMessageBus> _logger;
    private bool _disposed;

    public KafkaMessageBus(IOptions<MessagingOptions> options, ILogger<KafkaMessageBus> logger)
    {
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = options.Value.Kafka.BootstrapServers,

            // Do not report a write as durable until every in-sync replica has it. The default
            // (leader-only) would lose acknowledged messages whenever a leader fails over.
            Acks = Acks.All,

            // librdkafka retries internally on transient errors, which can reorder a batch and
            // duplicate writes. Idempotence makes those retries safe: the broker de-duplicates by
            // producer sequence number and preserves per-partition order.
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string?, byte[]>(config)
            .SetErrorHandler((_, error) =>
            {
                // Transport-level noise, raised out of band from any particular publish. The failing
                // ProduceAsync call reports the error to its own caller; this is only for visibility.
                if (error.IsFatal)
                    _logger.LogError("[Kafka] Fatal producer error: {Reason}", error.Reason);
                else
                    _logger.LogWarning("[Kafka] Producer error: {Reason}", error.Reason);
            })
            .Build();
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
    /// Publishes a ready-made envelope to an explicit topic, preserving its id, timestamp, and key.
    /// The consumer's dead-lettering uses this so the message on <c>{topic}.dlq</c> is recognisably
    /// the one that failed.
    /// </summary>
    internal async Task PublishEnvelopeAsync(MessageEnvelope envelope, string topic, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var message = new Message<string?, byte[]>
        {
            // Null key means round-robin across partitions — fine for messages that never opted into
            // ordering via IPartitionedMessage.
            Key = envelope.PartitionKey,
            Value = MessageSerializer.Serialize(envelope),
            Timestamp = new Timestamp(envelope.PublishedAtUtc.UtcDateTime)
        };

        try
        {
            // Completes once the brokers have acknowledged per Acks.All above, which is what lets
            // IMessageBus.PublishAsync promise durability rather than just "handed to the client".
            var result = await _producer.ProduceAsync(topic, message, ct);

            _logger.LogDebug("[Kafka] Published {MessageId} to {Topic}[{Partition}]@{Offset}.",
                envelope.MessageId, topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string?, byte[]> ex)
        {
            _logger.LogError(ex, "[Kafka] Failed to publish {MessageId} to {Topic}: {Reason}",
                envelope.MessageId, topic, ex.Error.Reason);

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // Block until queued messages are acknowledged. Without this, disposing at shutdown
            // discards anything still in librdkafka's send buffer — messages whose PublishAsync has
            // not returned yet, but which the caller is entitled to see either succeed or throw.
            _producer.Flush(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Kafka] Flushing the producer at shutdown failed; queued messages may be lost.");
        }

        _producer.Dispose();
    }
}

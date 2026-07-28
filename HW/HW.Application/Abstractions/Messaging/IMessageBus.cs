namespace HW.Application.Abstractions.Messaging;

/// <summary>
/// Names the logical stream a message travels on. Required on every published message type.
///
/// The name is the contract shared with other services and with the broker's own tooling, so it is
/// declared here rather than derived from the CLR type name — renaming or moving the record must not
/// silently repoint traffic at a different stream.
/// </summary>
/// <example><c>[Message("vocab.reviewed")]</c></example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageAttribute : Attribute
{
    public MessageAttribute(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic must be a non-empty name.", nameof(topic));

        Topic = topic;
    }

    /// <summary>Logical stream name, e.g. <c>vocab.reviewed</c>.</summary>
    public string Topic { get; }
}

/// <summary>
/// Opt-in ordering key. Messages sharing a key are delivered in publish order relative to each other.
///
/// Only Kafka can actually honour this (the key selects the partition, and a partition is an ordered
/// log). RabbitMQ has no equivalent — a queue with competing consumers reorders freely — so under the
/// RabbitMQ adapter the key is carried as a header for tracing but buys no ordering guarantee.
/// Implement this when ordering is a preference; do not build correctness on it unless the deployed
/// provider is Kafka.
/// </summary>
public interface IPartitionedMessage
{
    /// <summary>Messages with equal keys stay ordered; null or empty falls back to round-robin.</summary>
    string? PartitionKey { get; }
}

/// <summary>
/// Publishes messages to the configured broker.
///
/// One implementation is active per process, chosen by <c>Messaging:Provider</c> at startup —
/// RabbitMQ, Kafka, or a no-op. Callers never learn which; that is the whole point of the seam.
///
/// <para>
/// <b>Delivery is at-least-once, and only after the broker acknowledges.</b> A publish that returns
/// successfully means the broker durably accepted the message, not that a consumer ran. A publish
/// that throws may still have reached the broker — the failure could be in the acknowledgement path.
/// Consumers must therefore tolerate duplicates; see <see cref="IMessageHandler{TMessage}"/>.
/// </para>
///
/// <para>
/// <b>This is not transactional with the database.</b> Publishing inside a command handler that later
/// throws leaves the message published and the transaction rolled back. When a message must not
/// escape unless the transaction commits, write it through the outbox instead.
/// </para>
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publishes <paramref name="message"/> to the topic named by its <see cref="MessageAttribute"/>,
    /// returning once the broker has acknowledged the write.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TMessage"/> carries no <see cref="MessageAttribute"/>.
    /// </exception>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : class;
}

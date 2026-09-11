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
/// Opt-in correlation key: the business identity that ties a message to the others in its
/// conversation — a saga id, an order id, an aggregate id.
///
/// <para>
/// <b>It buys no ordering, and no routing.</b> The key travels with the message and surfaces on
/// <see cref="MessageContext.PartitionKey"/> and in the broker's management UI, which is what makes
/// one conversation's messages findable among everything else on a queue. That is the whole of it.
/// RabbitMQ delivers a queue's messages to whichever competing consumer is free, and retries run on
/// their own clock, so two messages sharing a key can and will be handled out of order and
/// concurrently.
/// </para>
///
/// <para>
/// Build correctness on the state you have already committed, not on arrival order: handlers that
/// tolerate seeing effects before causes, and a saga whose decisions are driven by its persisted
/// state rather than by which reply happened to land first.
/// </para>
/// </summary>
public interface IPartitionedMessage
{
    /// <summary>The conversation's identifier. Null or empty when the message belongs to none.</summary>
    string? PartitionKey { get; }
}

/// <summary>
/// Publishes messages to the configured broker.
///
/// One implementation is active per process, chosen by <c>Messaging:Provider</c> at startup — the
/// hand-rolled RabbitMQ adapter, MassTransit over the same broker, or a no-op. Callers never learn
/// which; that is the whole point of the seam.
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

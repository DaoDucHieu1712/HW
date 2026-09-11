namespace HW.Application.Abstractions.Messaging;

/// <summary>
/// Broker-neutral delivery metadata handed to a handler alongside the message.
/// </summary>
/// <param name="MessageId">
/// Stable identifier assigned at publish time and preserved across redeliveries — the same logical
/// message retried after a crash carries the same id. This is the key to deduplicate on.
/// </param>
/// <param name="Topic">Logical stream the message arrived on.</param>
/// <param name="PartitionKey">
/// Correlation key from <see cref="IPartitionedMessage"/>, when the publisher set one. Identifies
/// the conversation this message belongs to; it implies nothing about ordering.
/// </param>
/// <param name="DeliveryAttempt">
/// 1 on first delivery, incrementing per retry. Reaching
/// <c>Messaging:MaxDeliveryAttempts</c> sends the message to the dead-letter topic.
/// </param>
/// <param name="PublishedAtUtc">When the publisher handed the message to the broker.</param>
/// <param name="Headers">Envelope headers — correlation ids, tracing, and similar out-of-band data.</param>
public readonly record struct MessageContext(
    string MessageId,
    string Topic,
    string? PartitionKey,
    int DeliveryAttempt,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Handles one message type off the bus. Register with <c>services.AddMessageHandler&lt;TMessage, THandler&gt;()</c>;
/// the consumer subscribes to the topic named by <typeparamref name="TMessage"/>'s
/// <see cref="MessageAttribute"/>. Handlers are resolved from a fresh DI scope per message, so they
/// may depend on scoped services such as repositories.
///
/// <para>
/// <b>Handlers must be idempotent.</b> Delivery is at-least-once on both providers: a crash between
/// the handler finishing and the broker recording that fact redelivers the message. Key
/// deduplication off <see cref="MessageContext.MessageId"/>, which survives redelivery, and not off
/// any id generated inside the handler.
/// </para>
///
/// <para>
/// <b>Throwing means "retry me".</b> The adapter retries with backoff up to
/// <c>Messaging:MaxDeliveryAttempts</c>, then routes the message to <c>{topic}.dlq</c> and moves on.
/// A message that can never succeed — malformed payload, referenced row deleted — should therefore
/// be logged and returned normally rather than thrown, since retrying it only delays the queue to no
/// purpose.
/// </para>
/// </summary>
public interface IMessageHandler<in TMessage> where TMessage : class
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken ct);
}

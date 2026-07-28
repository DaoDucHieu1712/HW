using HW.Application.Abstractions.Messaging;

namespace HW.Infrastructure.Messaging;

/// <summary>
/// The provider adapters' internal contract: an <see cref="IMessageBus"/> that also accepts a
/// caller-supplied message id.
///
/// <para>
/// This exists for the outbox. <see cref="IMessageBus.PublishAsync"/> mints a fresh id per call,
/// which is right for a direct publish but wrong for a redelivery: when the processor retries a row
/// because the broker publish succeeded but marking <c>ProcessedOnUtc</c> did not, a new id would
/// present the same logical message to consumers as a different one and defeat the deduplication
/// that <see cref="IMessageHandler{TMessage}"/> tells them to rely on. Passing the outbox row's id
/// through makes the identity survive every retry.
/// </para>
///
/// <para>
/// Deliberately internal: <c>IMessageBus</c> stays the whole of the public surface, and nothing
/// outside Infrastructure should be choosing message ids.
/// </para>
/// </summary>
internal interface IBrokerBus : IMessageBus
{
    /// <param name="message">The message instance. Its type must carry <c>[Message]</c>.</param>
    /// <param name="messageType">
    /// The declared type, passed explicitly because the processor rehydrates messages as
    /// <see cref="object"/> and <c>message.GetType()</c> would resolve a proxy or subclass.
    /// </param>
    /// <param name="messageId">The id consumers will deduplicate on. Must be stable across retries.</param>
    Task PublishAsync(object message, Type messageType, string messageId, CancellationToken ct);
}

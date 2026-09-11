namespace HW.Application.Abstractions.Sagas;

/// <summary>
/// A message that belongs to a saga conversation. The correlation id is on the contract rather than
/// inferred from a header because it is business data: every participant echoes it back, and it is
/// the only thing that lets a reply arriving minutes later — on a different instance, after a
/// restart — find the stream it belongs to.
///
/// <para>
/// Implementations should also implement <c>IPartitionedMessage</c> returning <see cref="SagaId"/>.
/// That makes one saga's traffic findable as a single conversation in the broker's tooling and in
/// traces — it does <b>not</b> order it. RabbitMQ hands a queue's messages to whichever competing
/// consumer is free, so two steps of one saga can be handled at once, on different instances, in
/// either order. This is why the saga's decision methods read persisted state instead of assuming
/// the reply in hand is the next one due, and why a late or duplicate reply must be a no-op rather
/// than an error.
/// </para>
/// </summary>
public interface ISagaMessage
{
    string SagaId { get; }
}

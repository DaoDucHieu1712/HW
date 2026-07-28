namespace HW.Domain.Entities.Sagas;

/// <summary>
/// Record that a saga has already consumed a given broker message — the receiving half of the
/// exactly-once illusion the outbox builds on the sending half.
///
/// <para>
/// The outbox guarantees a message is sent <i>at least</i> once; nothing can guarantee exactly once
/// over a network. So the saga makes the redelivery harmless instead: the row is inserted in the
/// same transaction as the events the message produced, so either both are durable or neither is,
/// and the second delivery finds the row and returns without touching the state machine.
/// </para>
///
/// <para>
/// Keyed on <c>MessageId</c> alone, which is the outbox row id and therefore stable across every
/// redelivery of the same logical message — a fresh id per attempt would defeat the whole thing.
/// </para>
/// </summary>
public class SagaInboxEntry
{
    /// <summary>Primary key — <c>MessageContext.MessageId</c> as delivered by the broker.</summary>
    public string MessageId { get; set; } = string.Empty;

    public string SagaId { get; set; } = string.Empty;

    public DateTimeOffset HandledAtUtc { get; set; }
}

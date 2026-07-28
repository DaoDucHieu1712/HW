using HW.Domain.Abstractions.Entities;

namespace HW.Domain.Entities.Sagas;

/// <summary>
/// One row of one saga's stream — the event store's only write target. Append-only: nothing ever
/// updates or deletes a row here, because a fact that happened cannot stop having happened. Bugs are
/// fixed by appending a corrective event, not by editing history.
/// </summary>
public class SagaEventRecord : Entity
{
    /// <summary>Correlation id of the saga instance the event belongs to.</summary>
    public string SagaId { get; set; } = string.Empty;

    /// <summary>
    /// CLR type of the saga, so <c>LoadAsync</c> can tell streams apart and a diagnostic query can
    /// filter by saga kind without opening every payload.
    /// </summary>
    public string SagaType { get; set; } = string.Empty;

    /// <summary>
    /// Position in the stream, starting at 1 and dense. Unique per <see cref="SagaId"/> — that index
    /// is the optimistic-concurrency check, not just an ordering aid.
    /// </summary>
    public long Version { get; set; }

    /// <summary>Assembly-qualified payload type, in the same format the outbox uses.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON payload of the <c>ISagaEvent</c>.</summary>
    public string Content { get; set; } = string.Empty;

    public DateTimeOffset OccurredOnUtc { get; set; }

    /// <summary>
    /// Broker message that caused this event, when there was one. Not used by replay — it is here so
    /// that when a saga stalls you can walk backwards from the stream to the message that moved it.
    /// </summary>
    public string? CausationMessageId { get; set; }
}

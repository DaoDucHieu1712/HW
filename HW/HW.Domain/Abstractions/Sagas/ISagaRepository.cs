namespace HW.Domain.Abstractions.Sagas;

/// <summary>
/// The event store, expressed as the only three operations a saga needs. Deliberately not an
/// <c>IRepository&lt;T&gt;</c>: there is no row to update here, only a stream to append to, and
/// offering <c>Update</c> or <c>Remove</c> over an append-only log would be a lie.
/// </summary>
public interface ISagaRepository
{
    /// <summary>
    /// Rehydrates a saga by replaying its stream in version order, or returns <c>null</c> when the
    /// stream does not exist.
    /// </summary>
    Task<TSaga?> LoadAsync<TSaga>(string sagaId, CancellationToken ct = default)
        where TSaga : EventSourcedSaga, new();

    /// <summary>
    /// Appends <see cref="EventSourcedSaga.UncommittedEvents"/> starting at
    /// <see cref="EventSourcedSaga.OriginalVersion"/> + 1 and refreshes the queryable projection.
    ///
    /// <para>
    /// Nothing is saved here — the rows join the caller's transaction, exactly like
    /// <c>OutboxMessageBus</c>, so the saga's new state and the commands it triggers commit or roll
    /// back as one. If the expected version is already taken, the unique index on
    /// <c>(SagaId, Version)</c> fails the commit and the message is retried against fresh state.
    /// </para>
    /// </summary>
    Task AppendAsync(EventSourcedSaga saga, CancellationToken ct = default);

    /// <summary>
    /// Claims <paramref name="messageId"/> for this saga, returning <c>false</c> if it was already
    /// claimed.
    ///
    /// <para>
    /// Broker delivery is at-least-once, and a saga is the one place where a duplicate is expensive:
    /// replaying "payment charged" would advance the state machine a second time and charge again.
    /// The claim is written in the same transaction as the events, so it is impossible to record
    /// having handled a message whose effects rolled back.
    /// </para>
    /// </summary>
    Task<bool> TryClaimMessageAsync(string sagaId, string messageId, CancellationToken ct = default);
}

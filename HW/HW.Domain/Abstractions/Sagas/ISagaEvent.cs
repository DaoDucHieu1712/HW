namespace HW.Domain.Abstractions.Sagas;

/// <summary>
/// A fact that has already happened to a saga. The saga's stream of these <b>is</b> its state —
/// nothing else is authoritative, and the current state is always <c>fold(Apply, events)</c>.
///
/// <para>
/// <b>Deliberately not an <see cref="Events.IDomainEvent"/>.</b> A domain event is a notification
/// other parts of the system react to, and <c>OutboxMessageProcessor</c> routes it to MediatR. A saga
/// event is the saga's private write model: it is appended to the saga's own stream and replayed by
/// the saga alone. Making one an <c>IDomainEvent</c> would put every internal state transition on the
/// outbox and dispatch it in-process, which is both noise and a leak of the orchestrator's internals.
/// </para>
///
/// <para>
/// <b>Implementations must be immutable records and forever deserializable.</b> They are persisted
/// and replayed indefinitely: a stream written last year is read by today's code. Add new optional
/// properties; never rename, retype, or remove existing ones, and never change a type's name or
/// namespace without leaving a migration path — the stored <c>Type</c> column resolves to the CLR
/// type by name.
/// </para>
/// </summary>
public interface ISagaEvent
{
}

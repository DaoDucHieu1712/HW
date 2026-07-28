# 10 — Saga (orchestration) with an event-sourced orchestrator

A saga coordinates a workflow across services that do not share a database. There is no transaction
that can span them, so atomicity is replaced with a weaker property that is actually achievable:
**every step that succeeded before a failure has an explicit compensating action**, and the workflow
walks them back in reverse.

The orchestrator's state is **event-sourced** — a stream of facts, not a row. Current state is always
`fold(Apply, events)`.

```
HW.Domain/Abstractions/Sagas/     → ISagaEvent, EventSourcedSaga, ISagaRepository, SagaStatus
HW.Domain/Entities/Sagas/         → SagaEventRecord (stream), SagaInstance (projection), SagaInboxEntry (dedup)
HW.Application/Abstractions/Sagas/→ ISagaMessage, SagaMessageHandler<TSaga,TMessage>, SagaMessagePublisher
HW.Application/Features/Orders/   → OrderSaga, OrderSagaEvents, OrderSagaMessages, participants, command/query
HW.Infrastructure/Sagas/          → SagaRepository (the event store)
```

## The reference flow

```
POST /api/order
  └─ PlaceOrderCommand ─┐
                        ├─ append [Started, StockReserveRequested]   ┐
                        └─ outbox ReserveStock                       ├─ ONE transaction
                                                                     ┘
  ReserveStock ─► StockParticipant ─► StockReserved  ─► saga ─► ChargePayment
                                   └► StockRejected  ─► saga ─► Compensated (nothing to undo)

  ChargePayment ─► PaymentParticipant ─► PaymentCharged ─► saga ─► Completed
                                       └► PaymentFailed ─► saga ─► ReleaseStock ─► Compensated
```

Stock is reserved **before** payment on purpose: the cheap failure (no stock) then costs no
compensation at all.

## The two rules that make it work

**1. `Apply` folds, decision methods decide.** `Apply` runs again on every load, once per event ever
raised, so it must be a pure state transition — no clock, no generated ids, no `Send`. Decision
methods (`OnStockReserved`, …) run once, when a real message arrives; they call `Raise` and `Send`.
Put a `Send` inside `Apply` and every rehydration re-sends the saga's entire command history.

**2. Every decision method no-ops unless the saga is in the matching step.** This, not the inbox
alone, is what makes redelivery and out-of-order arrival safe.

```csharp
public void OnStockReserved(string reservationId)
{
    if (Step != OrderSagaStep.AwaitingStockReservation) return;   // ← the guard

    var now = DateTimeOffset.UtcNow;
    Raise(new StockReserveSucceeded(reservationId, now));
    Raise(new PaymentChargeRequested(now));
    Send(new ChargePayment(Id, CustomerId, Amount));
}
```

## Everything commits together

`SagaRepository` writes through the same `ApplicationDbContext` as `OutboxMessageBus`, so in one
local transaction you get: **the inbox claim + the new events + the outbox rows for the next
commands.** That single ACID commit at the edge is what gives a non-atomic distributed workflow a
dependable spine — only the hops *between* services are eventually consistent.

Split them and each seam loses a saga:

| Split | Failure |
|---|---|
| claim without events | message swallowed, saga hangs forever |
| events without outbox rows | saga believes it asked for something nobody was told to do |
| outbox rows without events | participant acts on a decision the saga has no record of |

`SagaMessageHandler` opens that transaction with `IUnitOfWork.ExecuteAsync`. **This is the documented
exception to "no `_uow` in handlers"** — that rule holds because `TransactionBehavior` wraps every
`IBaseCommand`, and a message handler is not a MediatR request. `PlaceOrderCommandHandler` *is*, so it
correctly has no `_uow`.

## Concurrency and idempotency

- **Optimistic concurrency** — unique index on `(SagaId, Version)`. Two replies racing to advance the
  same saga compute the same next version; the loser's transaction fails on the constraint and the
  broker redelivers it against the winner's state. It is a correctness mechanism, not an index.
- **Dedup** — `SagaInboxEntry` keyed on `MessageId` (which is the outbox row id, stable across
  redeliveries). Claimed in the same transaction as the events, so it is impossible to record having
  handled a message whose effects rolled back.
- **Participants derive ids** (`PAY-{sagaId}`) rather than generating them, so a redelivery cannot
  produce a second charge.

## Statuses

`Running` → `Completed`, or `Running` → `Compensating` → `Compensated`. `Failed` is the one that
needs a human: compensation itself could not complete, so the services are genuinely inconsistent.
Alert on `OrderSagaEvents.Failed`.

## Events are forever

Streams are replayed indefinitely — code reads events written years earlier.

- Add new **optional** properties only. Never rename, retype, or remove one.
- Never delete an `ISagaEvent` record type, even when nothing raises it any more; replay needs it.
- Never rename or move the type without a migration path — the stored `Type` column resolves by name.
- `Apply` throws on an unknown event rather than ignoring it: silently folding a partial history
  produces state the saga then acts on.

## The projection is disposable

`SagaInstance` exists so "list every saga stuck compensating for an hour" doesn't mean replaying
thousands of streams. It is rewritten from folded state on every append, so it cannot drift. Drop the
table and nothing is lost — every row is regenerable from `SagaEventRecord`. One append-only truth,
as many disposable read models as you need.

## Adding a saga

1. Events: `record X(...) : ISagaEvent` — past tense, carrying everything compensation will need.
2. Contracts: `[Message("topic")] record Y(string SagaId, ...) : ISagaMessage, IPartitionedMessage`.
3. Saga: `sealed class XSaga : EventSourcedSaga` — public parameterless ctor (the repository replays
   into it), a static `Start`, one guarded decision method per reply, and `Apply`.
4. Handlers: derive `SagaMessageHandler<XSaga, TReply>`, override `Decide`, one line each.
5. Register in `AddOrderSagaMessaging`-style extension **before** `AddMessaging()` (MassTransit seals
   the registry).

## Caveats

- **`POST` returns 202, never 201.** Nothing is created yet. With the outbox on, the first command
  reaches the broker on the processor's next poll (≤ ~10s), so an immediate status read still says
  `AwaitingStockReservation`. That lag is the price of the atomicity.
- **Ordering is a preference.** `PartitionKey => SagaId` only buys ordering on Kafka. The step guards
  are what make out-of-order arrival safe everywhere else.
- **`Provider: None` (the default) drops publishes.** The saga will start and then sit at
  `AwaitingStockReservation` forever. Run RabbitMQ or Kafka to see it move.
- **Compensations must not be refusable.** A compensation that can fail for a business reason leaves
  the saga nowhere to go. Design them to be retryable and safe to apply twice.

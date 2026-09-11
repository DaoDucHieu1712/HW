Asynchronous messaging trades immediate consistency for decoupling. The price is
paid in duplicates, reordering and partial failure -- design for them explicitly.

**Publishing**

- Raise domain events on the aggregate; dispatch them after the transaction that
  produced them commits.
- Write the message to an outbox table inside that same transaction. A publish
  that is not part of the transaction will eventually publish something that
  never happened -- or lose something that did.
- Name events in the past tense, after the fact: `OrderPlaced`, not
  `PlaceOrder`. An event is a record, not a request.
- Version the payload. Consumers you do not own will outlive your schema.

**Consuming**

- Assume at-least-once delivery. Every handler must be idempotent -- key on the
  message id or on a natural business key, and make a repeat a no-op.
- Assume reordering across partitions. Do not depend on the order of two events
  unless the transport guarantees it for that key.
- Handle poison messages deliberately: bounded retries, then a dead-letter queue
  with enough context to diagnose. Infinite retry is an outage amplifier.

**Diagnosing**

- Correlate with an id that survives the hop, carried in the message header and
  logged on both sides. Without it a distributed timeline cannot be reconstructed.
- When an event appears to be lost, check the outbox before the broker: an event
  that never left the database was never a messaging problem.

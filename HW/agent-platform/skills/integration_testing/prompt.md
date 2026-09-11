An integration test exists to catch what unit tests structurally cannot: wiring,
mapping, SQL, serialisation, and the transaction boundary.

**Use the real thing where it is the point**

- Real database (Testcontainers or a disposable local instance), real EF Core
  model, real migrations. An in-memory provider does not run your SQL and will
  not catch your mapping bug.
- Real HTTP pipeline via `WebApplicationFactory<TProgram>`, so routing, model
  binding, filters and auth are actually exercised.

**Substitute only what makes the test non-deterministic**

- Clock -- inject a fixed `IClock`; never assert on `DateTime.Now`.
- Outbound messaging and third-party HTTP -- record instead of send.
- Random and id generation, when the assertion depends on the value.

**Isolation**

- Each test owns its data. Either a fresh schema per class, or a transaction
  rolled back at the end -- pick one and apply it everywhere.
- Never rely on execution order, and never share mutable static state.

**Assert on the observable contract:** the status code, the response body, the
rows that ended up in the database, the message that was published. Not on
intermediate calls.

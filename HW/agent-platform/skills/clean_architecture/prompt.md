Dependencies point inward. Nothing in an inner layer knows an outer one exists.

**Where code belongs**

- `Domain` -- entities, value objects, domain events, invariants. No framework
  types, no persistence, no I/O. If a rule must always hold, it lives here.
- `Application` -- use cases, orchestration, the abstractions infrastructure
  implements (`IOrderRepository`, `IClock`, `IEmailSender`). Depends on Domain
  only.
- `Infrastructure` -- EF Core, HTTP clients, message buses, file systems. It
  implements Application's interfaces and is referenced only by composition.
- `Api` / host -- transport, serialisation, auth, DI wiring. It is the
  composition root and the only place that knows every layer.

**Signals that a boundary has been crossed**

- A `DbContext`, `HttpClient` or `IConfiguration` referenced from Domain or
  Application.
- An Application handler that constructs a concrete infrastructure type instead
  of receiving an abstraction.
- Domain types with EF attributes or JSON attributes on them -- mapping belongs
  in configuration, not on the entity.
- A "shared" or "common" project that everything references, so nothing has a
  real boundary at all.

**When adding code, decide in this order:** what invariant is this? (Domain) --
what use case does it serve? (Application) -- what does it need from the outside
world? (an Application abstraction, implemented in Infrastructure).

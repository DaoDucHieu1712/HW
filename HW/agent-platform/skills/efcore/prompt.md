**Mapping**

- Configure with `IEntityTypeConfiguration<T>`, not attributes on the entity --
  the domain type stays free of persistence concerns.
- Map private collection backing fields so aggregates keep their invariants
  instead of exposing a settable `List<T>`.
- Owned types for value objects. A value object with its own table is usually a
  modelling mistake.

**Querying**

- `AsNoTracking()` for every read that does not lead to a write. Tracking a read
  model costs memory and invites accidental writes.
- Project to a DTO in the query (`Select`) rather than loading the entity and
  mapping in memory -- it changes the SQL, not just the C#.
- `Include` chains across two or more collections produce a cartesian explosion.
  Use `AsSplitQuery()` or separate queries.
- A `foreach` that awaits a query per item is an N+1. Load the set once and join
  in memory.

**Migrations and transactions**

- One `SaveChangesAsync` per use case, called by the transaction behaviour.
- Never call `EnsureCreated` in anything but a throwaway test fixture.
- A migration that both changes schema and moves data must be reviewed as a
  data migration, with a rollback plan stated.

**When a query is slow,** read the generated SQL before changing the C#. The
fix is usually in the shape of the query, not in the indexes.

The MES is the system of record for what physically happened on the shop floor.
Its constraints come from the factory, not from the code.

**Core vocabulary**

- *Work order* -- the instruction to produce a quantity of a part.
- *Routing* -- the ordered operations a unit passes through.
- *Operation* / *step* -- work at one resource; it has a start, an end, an
  operator and an outcome.
- *Lot* / *serial* -- the identity of what was produced; traceability hangs off it.
- *Genealogy* -- which input lots went into which output unit. Auditors read this.
- *Scrap* and *rework* -- a unit leaving the flow, or re-entering it. Both are
  events, never a silent quantity adjustment.

**Invariants worth failing loudly for**

- Quantity is conserved: produced + scrapped + in-process must reconcile against
  what was started. A discrepancy is a defect, not a rounding issue.
- Operations complete in routing order; an out-of-order completion is a data
  problem to surface, not to sort away.
- Every state change is attributable -- who, when, at which resource. An
  unattributed change cannot be audited and should not be accepted.
- Time is recorded in UTC with the site's timezone kept alongside; shift
  boundaries and daylight-saving edges are real bugs in this domain.

**When analysing an incident,** anchor on the work order and the unit, then read
the operation events in order. The first anomaly in the sequence is usually the
cause; later errors are typically consequences of the state it left behind.

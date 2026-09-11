# SRS — <Feature name>

| | |
|---|---|
| **Id** | SRS-\<slug\> |
| **Version** | 0.1 |
| **Date** | YYYY-MM-DD |
| **Status** | Draft / In review / Agreed |
| **Related** | SRD-\<slug\>, issue #… |

---

## 0. Open questions

> ⚠️ Collect **every** `[ASSUMPTION]` in the document up here. The reader answers them in one pass
> instead of hunting through the whole document. An empty section means the requirements are
> completely clear — rarely true in a first draft.

| # | Question | Assumption in use | Impact if wrong |
|---|---|---|---|
| 1 | | | |

---

## 1. Context & problem

**The problem today:** <who is suffering from what — described as an observable phenomenon, not as
the solution you want>

**Why now:** <what changed that makes this worth doing at this moment>

**Current state in the code:** <what the related files/modules already have — `file:line`>

---

## 2. Scope

### 2.1 In scope
- …

### 2.2 **Out of scope** (mandatory)
- …

> Section 2.2 prevents an over-eager implementer better than any reminder in a prompt. Be concrete:
> *"No paging in this version"* — not *"limited scope"*.

---

## 3. Actors & permissions

| Actor | Description | May do | May **not** do |
|---|---|---|---|

---

## 4. User stories & acceptance criteria

### US-01 — <title>

> As a **\<actor\>**, I want **\<action\>**, so that **\<value\>**.

| # | Acceptance criterion | Verified by |
|---|---|---|
| AC-01.1 | Given \<state\>, When \<action\>, Then \<observable result\> | `dotnet test --filter …` |
| AC-01.2 | | |

> **Rule:** the "Verified by" column **must not be empty**. An unverifiable requirement is a wish,
> and it will spawn a loop that does not know when to stop.

### US-02 — …

---

## 5. Business rules

| # | Rule | Source | Handling on violation |
|---|---|---|---|
| BR-01 | | | |

---

## 6. Error cases & boundaries

> The part where production breaks lives here, not in the happy path.

| # | Situation | Expected behaviour | Message to the user |
|---|---|---|---|
| EX-01 | The data does not exist | | |
| EX-02 | The user lacks permission | | |
| EX-03 | An external system does not respond | | |
| EX-04 | Duplicate submit / double click | | |

---

## 7. Non-functional requirements (**must have numbers**)

| Kind | Requirement | Measured by |
|---|---|---|
| Performance | p95 < … ms with … records | |
| Concurrency | … requests/s | |
| Security | | |
| Compatibility | | |
| Logging/audit | | |

---

## 8. Dependencies & risks

| Dependency | Kind | Risk if absent |
|---|---|---|

---

## 9. Traceability

| US / AC | SRD section | Code | Test |
|---|---|---|---|

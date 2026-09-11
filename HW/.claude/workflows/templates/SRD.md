# SRD — <Feature name>

| | |
|---|---|
| **Id** | SRD-\<slug\> |
| **Source SRS** | SRS-\<slug\> |
| **Version** | 0.1 |
| **Date** | YYYY-MM-DD |
| **Status** | Draft / In review / Agreed |

---

## 1. Design summary

<3–5 sentences: what the approach is, and why it is this one rather than another>

---

## 2. Decisions & trade-offs

| # | Decision | Chosen | Rejected option | Why | **Trade-off accepted** |
|---|---|---|---|---|---|
| D-01 | | | | | |

> The last column is the most important one and the one most often left blank. **A decision with no
> trade-off means you do not understand it yet.**

---

## 3. Architecture

### 3.1 Place in the system
```
<block diagram: where the request enters, which layers it crosses, what it touches>
```

### 3.2 New / changed components

| Component | Layer | New/Changed | Responsibility |
|---|---|---|---|

---

## 4. Data model

### 4.1 Table / entity

| Column | Type | Null? | Default | Note |
|---|---|---|---|---|

### 4.2 Indexes & constraints

| Name | Kind | Columns | Why it is needed |
|---|---|---|---|

> An index that cannot name the query it serves is a redundant index. So is a constraint that cannot
> name the business rule it protects.

---

## 5. API contract

### `<METHOD> /path`

**Request**
```json
{ }
```

**Response 200**
```json
{ }
```

**Error codes**

| HTTP | code | When | Body |
|---|---|---|---|
| 400 | | | |
| 404 | | | |
| 409 | | | |

---

## 6. Processing flow

### 6.1 Main case
```
1. …
2. …
```

### 6.2 Error cases (mandatory — this is where production breaks)

| Case | Happens at step | Handling | State left behind |
|---|---|---|---|

### 6.3 Idempotency & side effects

> Both agents and users can retry. If this operation is not idempotent, one retry = one duplicate
> (AG-17).

| Operation | Idempotent? | Mechanism | Keyed on |
|---|---|---|---|

---

## 7. Implementation plan

| # | Step | File | **Verifier (a runnable command)** |
|---|---|---|---|
| 1 | | | `dotnet test --filter …` |

> "Double-check that it is right" is **not** a verifier.

---

## 8. Migration & rollback

| | |
|---|---|
| **Schema change** | |
| **Backwards compatible** | yes / no — if no, why that is acceptable |
| **Deployment order** | |
| **Rollback** | <the concrete steps back to the previous state> |

> A schema change with no way back is a risk that is not permitted.

---

## 9. Observability

| What we need to know in production | Log/metric/trace | Alert threshold |
|---|---|---|

---

## 10. Remaining risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|

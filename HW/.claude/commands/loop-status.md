---
description: See where the loop is, and where the loop is broken
---

## Loop state

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Task.ps1 -Action status`

## Operational metrics

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Trace.ps1`

## Task

Read the numbers above and answer the **three operational questions** (AG-12) — not "accuracy":

| Metric | What it tells you |
|---|---|
| **Turns per completed task** (p50/p95) | a spike means the model is **flailing** => the tool or the prompt is broken, not the model |
| **Cost per completed task** | the **only** economic metric worth tracking — not cost/request |
| **Tool error rate per tool** | the tool that errors often is the tool with a **bad description/schema** |

Also: the rate of **hitting the budget ceiling** (hitting it often means the ceiling is wrong **or**
the agent is stuck), and the number of **thrashing** events.

If you find a problem, propose fixes in **order of leverage** — and **change only one variable per
round**:

1. verifier (add one / tighten it) ★★★★★
2. tool descriptions ★★★★
3. budget / stop conditions ★★★
4. compaction strategy ★★★
5. effort / model ★★
6. prompt wording ★

Change two variables at once and the result cannot be attributed to either — and you have burned a
measurement round.

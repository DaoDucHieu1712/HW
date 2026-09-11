---
name: trace-error-log
description: Reconstruct the sequence of events from error logs and SQL traces — follow the correlation id, read the original stack trace, tell cause apart from consequence, and settle it with quotable evidence. Use when the user pastes an exception/stack trace, says "trace the log", "read the log", "why a 500", "server error", "what did this request do", or when an incident timeline is needed.
argument-hint: "[stack trace / correlation id / incident description]"
---

# Trace error log — reconstruct what happened

> **Logs say what happened, not what caused it.**
> The first error line you see is usually the *consequence*, not the *cause*.

The goal of this skill is **an evidence-backed timeline**, not a conclusion. Fixing is
[fixbug](../fixbug/SKILL.md)'s job — only move there once the timeline stands up.

## 0. The tracing infrastructure that exists in the solution

The application keeps its own logs and SQL in ring buffers and exposes them to humans and agents
alike:

| Source | Obtained via | Code |
|---|---|---|
| Application log (ring buffer) | `GET /api/diagnostics/logs` | `HW.Infrastructure/Diagnostics/RingTraceStore.cs` |
| SQL executed (ring buffer) | `GET /api/diagnostics/sql` | `HW.Infrastructure/Diagnostics/SqlTraceInterceptor.cs` |
| Correlation id on every request | header `X-Correlation-ID`, log field `CorrelationId` | `HW.Api/Middlewares/CorrelationIdMiddleware.cs` |
| An agent reading the log for you | the `trace_log` tool | `HW.Agentic/Tools/TraceLogTool.cs` |
| An agent reading the SQL for you | the `trace_sql` tool | `HW.Agentic/Tools/TraceSqlTool.cs` |

The buffers live in process memory: **a restart loses them**. Incident just happened but the app has
restarted -> say immediately that the evidence is gone; do not keep reasoning as though it were
still there.

Need to sweep logs and SQL widely at the same time -> delegate to the `log-tracer` and `sql-tracer`
agents in parallel (the `triage-incident` workflow in `HW.Agentic/Workflow/WorkflowCatalog.cs`
already does this).

## 1. Anchor on a correlation id

First thing, before reading a single line: **determine which request**.

- Have a correlation id -> filter by it. Done.
- Only a stack trace -> work back via timestamp + exception type + route.
- Nothing to anchor on -> say plainly that you are reading **unfiltered** logs, and that every
  correlation after that is speculation.

Reading unanchored logs is the fastest way to weld two requests into one story.

## 2. Read the **original** stack trace, all of it

- The first line = **where it blew up**. The stack = **how it got there**. You need both.
- `InnerException` is where the real information is; the wrapping `AggregateException` is almost
  always meaningless.
- Skip framework frames; the **first frame belonging to `HW.*`** is where opening the code pays off.
- **Do not summarise the stack trace when passing it on.** Paste it verbatim.

## 3. Build the timeline before concluding

```
| # | t (ms) | Source | Event (verbatim quote) | Note |
```

Ordered by time, **not** by severity. Patterns in this table worth a closer look:

| What you see in the timeline | Usually means |
|---|---|
| Many near-identical SQL statements differing only in a parameter | N+1 — missing `Include`/projection |
| A `WARN` appearing a few dozen ms **before** the `ERROR` | the `WARN` is the actual cause |
| An identical exception repeating on a steady cycle | a retry/consumer looping, not a new failure |
| Logs stop abruptly mid-request | timeout / process death, not a swallowed exception |
| SQL runs but there is no commit | the transaction was rolled back higher up |

## 4. Separate cause from consequence

For each ERROR line, ask exactly one question: **"does this explain the line before it?"**

- Yes -> keep working backwards.
- No -> this may be the root cause, or you are looking at two different incidents.

Stop working backwards when you reach a line that **nothing before it explains**, and you may only
enter the code from there.

## 5. Settle it with evidence, not with a feeling

Every claim in the conclusion must be tied to **one quotable line**. Nothing backing it -> label it
`SPECULATION:` right inside that sentence. Do not blend the certain part with the guessed part.

## When stuck

Read all the logs and still cannot build a continuous timeline — pick **one**:

1. **Widen the window** — go further back before the incident; the cause is usually earlier than
   expected.
2. **Change source** — what the log does not show, the SQL trace may, and vice versa.
3. **Add logging and reproduce** — say exactly where logging is needed rather than guessing through
   the dark.
4. **Stop and report** — how far you got, where it breaks, what you need to continue.

## Report

```
## Incident
<symptom>, correlation id `<id>` (or: COULD NOT ANCHOR — every correlation is speculation)

## Timeline
| # | t | Source | Event | Note |
|---|---|--------|-------|------|

## Root cause
`file.cs:line` — <mechanism>
Evidence: <verbatim quote of the log/SQL line>

## Consequences (NOT the cause)
- <the noisiest error line and why it is only a consequence>

## Unresolved
- SPECULATION: <what, and what evidence would settle it>

## Next step
-> `/fixbug` with the root cause above
```

The "Consequences" section exists because the noisiest error line is almost always the thing that
gets fixed by mistake.

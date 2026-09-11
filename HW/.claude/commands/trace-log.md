---
description: Reconstruct the timeline of an incident from logs and SQL traces — anchor on a correlation id, separate cause from consequence
argument-hint: "[stack trace / correlation id / incident description]"
---

## Is the application running (the trace buffers live in-process)

!`powershell -NoProfile -Command "$p = Get-Process -Name 'HW.Api' -ErrorAction SilentlyContinue; if ($p) { 'HW.Api is running: PID ' + $p.Id + ' - the ring buffer still has data' } else { 'HW.Api is NOT running - the ring buffer is gone, only on-disk logs remain, if any' }"`

## Incident

$ARGUMENTS

## Task

Follow the `trace-error-log` skill. The goal is **an evidence-backed timeline**, not a conclusion.
Fixing is `/fixbug`'s job — only move there once the timeline stands up.

1. **Anchor on a correlation id before reading a single line.** Header `X-Correlation-ID`, log field
   `CorrelationId`. Cannot anchor -> say plainly that you are reading unfiltered logs and that every
   correlation is speculation.
2. **Collect evidence** from `GET /api/diagnostics/logs` and `GET /api/diagnostics/sql`
   (`HW.Infrastructure/Diagnostics/`). Need to sweep both sources widely at once -> delegate to the
   `log-tracer` agent (read-only).
3. **Read the original stack trace, all of it.** `InnerException` is where the real information is.
   The first frame belonging to `HW.*` is the place worth opening the code. **Do not summarise the
   stack trace.**
4. **Build the timeline chronologically**, not by severity.
5. **Work backwards from the ERROR**: for each line ask *"does this explain the line before it?"*.
   Stop at the first line that nothing before it explains.
6. **Every claim is tied to a quotable line.** None -> label it `SPECULATION:` inside the sentence.

## Forbidden

**Do not change code in this command.** Finish with a proven root cause, then move on to `/fixbug`.

## Final report

Timeline (table, verbatim quotes) · root cause at `file:line` + the log line backing it ·
**the consequence that gets mistaken for the cause** · what remains speculation and what evidence
would settle it · evidence that could not be obtained (and why).

# `state/` — loop state, kept **outside** the context

## Why state must live on disk

1. **Context gets compacted.** After compaction the details are gone — files are not.
2. **Subagents start cold.** They need somewhere to read "where are we" without asking again.
3. **Stop conditions must be verifiable from the outside.** A number in the context is not a
   constraint; a number in a file that a hook can read is.

## The files here

| File | Written by | Contents |
|---|---|---|
| `CURRENT_TASK.md` | **you** (copied from `workflows/templates/TASK.md`) | the open task — the `SessionStart` hook loads it into context |
| `loop-run.json` | `functions/Task.ps1` | current step, turn count, budget |
| `loop-progress.json` | `hooks/loop-progress.ps1` | hash(tool+args) -> failure count, for thrashing detection |
| `audit.jsonl` | `hooks/audit-log.ps1` | every tool call + everything blocked |
| `subagents.jsonl` | `hooks/subagent-stop-log.ps1` | every delegation — multi-agent economics data |
| `compaction.jsonl` | `hooks/pre-compact-snapshot.ps1` | a snapshot before each context compaction |
| `runs/*.json` | `functions/Task.ps1 -Action close` | the history of closed runs |

## Should any of this be committed?

**No.** See the `.gitignore` next to this file — `CURRENT_TASK.md` is **yours**, the logs belong to
**your machine**. What is worth committing is `workflows/templates/TASK.md` (the template), not a
filled-in copy.

## Reading the numbers

```powershell
powershell -NoProfile -File .claude/functions/Trace.ps1
```

Or `/loop-status` inside a session.

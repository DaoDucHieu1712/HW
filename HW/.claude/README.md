# `.claude` — LoopAgentic for the HW solution

Four dev loops, built on one framework: **gather -> act -> verify -> stop**.

That framework lives in [`skills/loop-agentic/SKILL.md`](skills/loop-agentic/SKILL.md). Read it once
before using any of the loops below — it says *when to stop*, and that is the part most often
missing.

## The four loops

| Job | Command | Skill | Workflow | Specialist agent |
|---|---|---|---|---|
| Generate unit tests | `/gen-test` | [unit-test](skills/unit-test/SKILL.md) | `workflows/unittest-loop.json` | `test-writer` |
| Fix a bug | `/fixbug` | [fixbug](skills/fixbug/SKILL.md) | `workflows/fixbug-loop.json` | `bug-hunter` |
| Read an error log | `/trace-log` | [trace-error-log](skills/trace-error-log/SKILL.md) | `workflows/trace-loop.json` | `log-tracer` |
| New feature / function | `/feature`, `/func` | [feature-dev](skills/feature-dev/SKILL.md) | `workflows/feature-loop.json` | `implementer` |

Support commands: `/verify` (run the verifier), `/review` (review the changes), `/loop-status`
(where the loop is).

### How they connect

```
   an error log, cause unknown
            |
            v
      /trace-log --------> root cause at file:line
            |                        |
            |                        v
            |                    /fixbug -------> red test -> minimal fix -> green
            |
   a new request ---> /feature (or /func) ---> vertical slice + tests in step
                              |
                              v
                         /gen-test  (backfill coverage for existing code)
```

`/trace-log` is **read-only** — it stops at the root cause and hands over. The split is deliberate:
an agent that reads logs and is also allowed to change code stops reading the moment it finds the
first plausible thing.

## The verifier — "done" is an exit code, not a sentence

```powershell
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level build   # fast, after every edit
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level full    # before declaring done
```

| Tier | Verifier | Here it is |
|---|---|---|
| A | compiler | `dotnet build HW.slnx` |
| B | tests | `dotnet test HW.UnitTests` |
| C | real state | call the endpoint again / re-query the DB |
| E | human approval | migrations, broker, real data |

The concrete commands live in `settings.json` -> `env.LOOP_BUILD_CMD` / `LOOP_TEST_CMD`.

> **Common trap:** while `HW.Api` is running, `dotnet build HW.slnx` fails with
> `MSB3021`/`MSB3027` — that is a **locked DLL file**, not a compile error. Kill the process and
> re-run. Only `CS####` is a real code error.

## Loop state lives outside the context

```powershell
powershell -NoProfile -File .claude/functions/Task.ps1 -Action init -Workflow feature-loop -Title "..."
powershell -NoProfile -File .claude/functions/Task.ps1 -Action step -Step implement
powershell -NoProfile -File .claude/functions/Task.ps1 -Action status
powershell -NoProfile -File .claude/functions/Task.ps1 -Action close -Outcome done
```

Context gets compacted; files do not. `Trace.ps1` reads `state/*.jsonl` and prints the three
operational metrics: turns/task, the tools that error most, and the thrashing count.

## Hooks currently enabled

| Event | Hook | What it does |
|---|---|---|
| SessionStart | `session-start-context.ps1` | loads the session context |
| UserPromptSubmit | `prompt-guard.ps1` | flags dangerous prompts |
| PreToolUse(Bash) | `block-dangerous.ps1` | blocks destructive commands |
| PreToolUse(Read/Grep/Glob) | `protect-secrets.ps1` | blocks reading secrets |
| PostToolUse(Edit/Write) | `format-after-edit.ps1` | `dotnet format` |
| PostToolUse(*) | `audit-log.ps1` | writes `state/audit.jsonl` |
| PostToolUseFailure | `loop-progress.ps1` | **stops thrashing** (same tool + args + error x3) |
| Stop | `stop-quality-gate.ps1` | build + test before the turn ends |
| SubagentStop | `subagent-stop-log.ps1` | records multi-agent economics |
| PreCompact | `pre-compact-snapshot.ps1` | snapshots the state before compaction |

`loop-progress.ps1` is the most valuable hook here: repeating identically 3 times means the agent is
**stuck**, not **trying**.

## Solution map

| Project | Contains | May reference |
|---|---|---|
| `HW.Domain` | entities, value objects, domain events | — |
| `HW.Agentic` | the **agent runtime**: loop, tools, workflows, LLM providers | *(no project)* |
| `HW.Application` | CQRS, validators, DTOs, per-feature agents | Domain, Agentic |
| `HW.Infrastructure` | EF, repositories, messaging, tool stores | Application, Domain, Agentic |
| `HW.Api` | controllers, middleware, DI | everything |
| `HW.UnitTests` | tests | everything |

`HW.Agentic` does not reference back up — `HW.UnitTests/Agentic/AgenticIsolationTests.cs` enforces
it.

## Relationship with `HW.CS/.claude`

`HW.CS/.claude` is the **teaching/template** set (with the `ai-loop-agentic` plugin), scoped to
`HW.CS/`. This set sits at the repo root, is scoped to the whole solution, and focuses on the four
everyday dev jobs. The hooks and `functions/*.ps1` were ported over unchanged — they all use
`$env:CLAUDE_PROJECT_DIR`, so they work correctly in both places.

The spec skills (`spec-srs`, `spec-srd`, `mockup`, `code-analysis`, `eval-harness`, `report`) stay in
`HW.CS/.claude/skills/` — not duplicated, so the two copies cannot drift apart.

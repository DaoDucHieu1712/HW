---
name: loop-agentic
description: The agentic loop framework for the HW solution — gather -> act -> verify -> stop, plus the four dev loops already built (unit-test, fixbug, trace-error-log, feature-dev). Use at the start of any multi-step task, when choosing a loop/agent, when a loop drifts/repeats/burns budget, or when the user says "loop", "agentic", "run the pipeline", "just do it".
argument-hint: "[task description]"
---

# Loop Agentic — the operating framework for the HW solution

> **Agent = LLM + tools + a loop + stop conditions.**
> Drop the loop and it is an API call. Drop **verify** and it is a machine for generating confident
> actions.

## 0. Pick the right loop

| What the user wants | Loop | Command | Skill |
|---|---|---|---|
| Generate unit tests for existing code | `unittest-loop` | `/gen-test` | [unit-test](../unit-test/SKILL.md) |
| Fix a bug whose symptom is known | `fixbug-loop` | `/fixbug` | [fixbug](../fixbug/SKILL.md) |
| Reconstruct what happened from logs | `trace-loop` | `/trace-log` | [trace-error-log](../trace-error-log/SKILL.md) |
| Write a new feature / new function | `feature-loop` | `/feature`, `/func` | [feature-dev](../feature-dev/SKILL.md) |

Unclear which kind it is -> ask one question, do not guess. Guessing the wrong loop costs a whole
loop, not one sentence.

Before climbing to the agent tier, go left to right and **stop at the first tier that works**:

```
one call  ->  workflow (fixed steps)  ->  agent (the model decides the flow)
```

| Criterion | If "no" |
|---|---|
| Multi-step task that **cannot be specified up front**? | use a workflow |
| Is the result worth the higher cost + latency? | use one call |
| Can the model actually do this kind of work? | do not build it, it will fail expensively |
| **Are errors detectable and recoverable?** | a human must approve |

Criterion 4 matters most and is the one most often skipped. This solution has cheap, fast verifiers
— `dotnet build`, `dotnet test HW.UnitTests` — so most coding work here **meets** criterion 4. Work
that touches migrations, the message broker, or production data **does not**.

## 1. The loop

```
        +----------------------------------------------+
        v                                              |
  +--------------+                                     |
  | 1. GATHER    |  read files, grep, git log          |
  |    CONTEXT   |  -> `explorer` agent if a lot must be read
  +------+-------+                                     |
         v                                             |
  +--------------+                              +------+-------+
  | 2. ACT       |----------------------------->| 3. VERIFY    |
  |    change 1  |                              | Verify.ps1   |
  |    small unit|                              | (tier A/B/C) |
  +--------------+                              +------+-------+
                                          pass? --------+---- no -+
                                            |
                                            v
                                       4. REPORT
```

**Run the verifier after every small unit**, not once at the end.

```powershell
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level build   # fast, after every edit
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level full    # before declaring done
```

> **Repo-specific note:** if `HW.Api` is running, `dotnet build HW.slnx` will **fail at the DLL copy
> step** (MSB3021/MSB3027), not at compilation. Read the error code carefully: `MSB302x` = the file
> is locked, kill the `HW.Api` process and re-run. `CS####` is a real code error.

## 2. The verifier ladder — always use the highest tier available

| Tier | Verifier | In this solution |
|---|---|---|
| **A** | compiler / linter | `dotnet build HW.slnx` |
| **B** | unit / integration test | `dotnet test HW.UnitTests` |
| **C** | assert against real state | call the endpoint again, re-query the DB |
| **D** | LLM-as-judge against a rubric | only when A–C are unavailable |
| **E** | human approval | migrations, broker, real data |

When the verifier FAILS: carry the error output **verbatim** into the next step. Do not summarise —
the model needs to read exactly what the compiler said.

## 3. Stop conditions — six of them; missing one burns money

| # | Stop when | Mechanism in this repo |
|---|---|---|
| 1 | The turn ceiling is hit | `Task.ps1 -Action step` -> exit 2 |
| 2 | The budget ceiling is hit | `LOOP_MAX_TURNS` in `settings.json` |
| 3 | Wall-clock time exceeded | the hook's `timeout` |
| 4 | **No progress** | the `loop-progress.ps1` hook (same tool + same args + same error x3) |
| 5 | The goal is reached | `Verify.ps1` exits 0 |
| 6 | **A human is needed** | escalate — see §5 |

Condition 4 is the one most often missing. Repeating identically 3 times means the agent is **stuck**,
not that it is **trying**.

## 4. Six failure modes — diagnose from the symptom

| Symptom | Failure mode | Cure |
|---|---|---|
| Same tool, same args, same error, repeatedly | **Thrashing** | change hypothesis, or stop and report |
| Gets more confused as it runs, clinging to a bad assumption from turn 5 | **Context poisoning** | restart clean with a **verified** summary |
| Refactors the whole module when asked to fix one function | **Over-eager** | state the scope + the bans explicitly |
| Reports done at the halfway point | **Stopping short** | define "done" = a runnable verifier |
| Ignores available tools and invents data | **Tool blindness** | rewrite the tool descriptions; reduce/merge tools |
| The tool threw but the model believed it succeeded | **Silent failure** | always return the real error, never swallow exceptions |

**Diagnose by reading the trajectory** (the sequence of tool calls + results), **not** the final
answer. The final answer always sounds plausible — that is precisely the problem.

## 5. Escalation is a first-class action

Stopping to ask a human **is not a failure**. Escalate when:

- an irreversible action has no explicit approval (migration, `git push`, deleting data);
- all three hypotheses are rejected and there is no fourth;
- the request is ambiguous enough that two readings lead to substantially different work.

When escalating, supply all of it: **what was tried -> how it failed -> what decision is needed**.
Do not just say "I'm stuck".

## 6. Loop state

```powershell
powershell -NoProfile -File .claude/functions/Task.ps1 -Action init -Workflow feature-loop -Title "..."
powershell -NoProfile -File .claude/functions/Task.ps1 -Action step -Step implement
powershell -NoProfile -File .claude/functions/Task.ps1 -Action close -Outcome done
```

State lives **outside the context** because context gets compacted and files do not.

## 7. Solution map — know where code goes before writing it

| Project | Contains | May reference |
|---|---|---|
| `HW.Domain` | entities, value objects, domain events, exceptions | — |
| `HW.Agentic` | the **agent runtime**: loop, tools, workflows, LLM providers | *(no project)* |
| `HW.Application` | CQRS handlers, validators, DTOs, per-feature agents | Domain, Agentic |
| `HW.Infrastructure` | EF, repositories, messaging, stores for agent tools | Application, Domain, Agentic |
| `HW.Api` | controllers, middleware, startup DI | everything |
| `HW.UnitTests` | tests for all four layers above | everything |

`HW.Agentic` **must not** reference back up — a test enforces that
(`HW.UnitTests/Agentic/AgenticIsolationTests.cs`). A feature's agent registers its own definition
with `services.AddSingleton(MyAgent.Definition())` instead of editing `AgentCatalog`.

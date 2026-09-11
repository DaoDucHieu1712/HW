# `workflows/` — **deterministic** pipelines, and knowing when not to use them

## The five standard workflow patterns (call them by name) — AG-1

| Pattern | Shape | Used for | Here it is |
|---|---|---|---|
| **Prompt chaining** | A -> B -> C | a task that splits into clear linear steps | `feature-loop` (domain -> app -> infra -> api) |
| **Parallelization** | fan-out -> fan-in | several independent viewpoints | `trace-loop` (log + SQL at once) |
| **Evaluator–optimizer** | generate -> grade -> fix, repeat | there is a clear grading criterion | `fixbug-loop` (the red test is the evaluator) |
| **Generator + mutation check** | generate -> try to break -> confirm | the output must be proven non-empty | `unittest-loop` |
| **Routing** | classifier -> specialised branch | different intents need different handling | `skills/loop-agentic` §0 |

The four loops here: [`unittest-loop`](unittest-loop.json) · [`fixbug-loop`](fixbug-loop.json) ·
[`trace-loop`](trace-loop.json) · [`feature-loop`](feature-loop.json). See
[`../README.md`](../README.md) for how they connect to each other.

## Workflow or agent?

The difference is **who decides the next step**:

| | **Workflow** | **Agent** |
|---|---|---|
| Who decides the flow | **your code/definition** (fixed) | **the model** (decided at runtime) |
| Predictable | ✅ | ❌ |
| Cost | known in advance | variable — **needs a ceiling** |
| Debugging | like ordinary code | you must **read the trajectory** |
| Suits | a task you already know how to do | a task that **cannot be specified up front** |

> **Start at the simplest tier that works: one call -> workflow -> agent.**
> Using an agent where a workflow suffices is volunteering to pay more money, more latency and more
> nondeterminism.

The JSON files here are **not an engine** — they are **readable contracts** describing the steps,
outputs, verifiers and exit criteria. The model reads them; `functions/Task.ps1` tracks position and
budget. No engine enforces the order, because what is worth enforcing (a red build must not end the
turn) lives in the **hooks**.

## The shape of a step

```jsonc
{
  "id": "act",
  "goal": "…",                       // one sentence, verifiable
  "agent": "implementer",            // who it is delegated to (omit = the main thread does it)
  "outputs": ["…"],                  // concrete artefacts, not "step completed"
  "verifier": "a runnable command",  // NOT "double-check that it is right"
  "exitCriteria": "…"                // when it may move on
}
```

The **`verifier`** field is the most important one. A step without a verifier is a step that does not
know when it is finished — and that is exactly how an agent ends up "stopping short" (AG-8).

## Where the stop conditions live

| Kind | Enforced by | Where |
|---|---|---|
| Turn ceiling | `functions/Task.ps1` (exit 2) | `LOOP_MAX_TURNS` |
| No progress | `hooks/loop-progress.ps1` (exit 2) | `LOOP_MAX_SAME_ERROR` |
| Goal reached | `functions/Verify.ps1` (exit 0) | — |
| Cannot finish while red | `hooks/stop-quality-gate.ps1` (exit 2) | `LOOP_QUALITY_GATE=1` |
| A human is needed | `permissions.ask` + `hooks/block-dangerous.ps1` | `settings.json` |

Note: **none of these is a sentence in a prompt.** That is deliberate — stop conditions belong to the
deterministic layer (HR-1).

## `templates/`

| File | Used by |
|---|---|
| `SRS.md` | the `analyst` agent; the `spec-srs` skill in `HW.CS/.claude/skills/` |
| `SRD.md` | the `architect` agent; the `spec-srd` skill in `HW.CS/.claude/skills/` |
| `TASK.md` | the template for filling in `.claude/state/CURRENT_TASK.md` |
| `REPORT.md` | the `reporter` agent |

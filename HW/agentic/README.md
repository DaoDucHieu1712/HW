# Loop Agentic

A LangGraph feature-development loop for the HW solution. You describe a feature; it explores the
codebase, writes a plan you approve, implements it in an isolated git worktree, compiles it, runs the
tests, repairs its own failures, has a second model review the result, and hands you a diff to accept.

Two points in that sequence stop and wait for a human. Nothing reaches your working tree without
passing both.

```
                       ┌──────────────────── revise ─────────────────────┐
                       ▼                                                 │
  START ─► prepare ─► scout ─► make_plan ─► plan_gate ══[HUMAN]══► code ─┤
                                                │                        │
                                             reject                      ▼
                                                │              ┌─► verify_build ─┐
                                                ▼              │        │        │
                                               END          repair ◄────┤     (passes)
                                                               ▲        │        ▼
                                                               │     (fails)  verify_tests ─┐
                                                               │                   │        │
                                                               ├───────────────────┤     (passes)
                                                               │                (fails)     ▼
                                                               │                      review_change
                                                               │                            │
                                                               ├──── request_changes ───────┤
                                                               │                        (approve)
                                                               │                            ▼
                                                               │                         propose
                                                               │                            │
                                                               └──── revise ── patch_gate ══[HUMAN]══► END
                                                                                    │
                                    budget spent at any repair ─► exhausted ─► END   └─ reject ─► END
```

## Why it is built this way

**The verification nodes are not agents.** `verify_build` and `verify_tests` run your configured
commands in a subprocess and write the real exit code into graph state. An agent can *claim* its
change works; it has no way to make the graph believe it. Every path that ends in an approved change
passed through a real process exiting zero.

**The loop writes into a git worktree, never your checkout.** `prepare` cuts a branch from `HEAD`
into `agentic/.state/worktrees/`. Everything the coder and fixer do happens there. A run that goes
wrong — a fixer that deletes the wrong file, a run abandoned halfway — costs you a `git worktree
remove`, not your working tree. This is what makes it safe to give an LLM a `write_file` tool at all.

**Every cycle is bounded.** The repair loop stops after `maxRepairAttempts` and reports `blocked`
with the last failure. A loop whose only exit is success does not terminate on the tasks where
termination matters most.

**State is durable, so the gates are real gates.** Checkpoints go to SQLite. A run can pause at the
plan gate, the process can exit, and you can resume the next morning. With in-memory state the gates
would be "answer now or lose the work", which is not review.

## Relationship to the C# agent stack

`HW.Application/Agents` already has a good in-process multi-agent system — `EngineerLoop`,
`WorkflowEngine`, the agent catalog, the patch-proposal queue. This does not replace it. It adds the
things a *feature-development* loop needs that a request-scoped one does not: cycles, durable state,
human interrupts, and real build/test verification.

It reuses the C# side for the two things that genuinely live there:

| Capability | Where it lives | How the loop reaches it |
|---|---|---|
| `trace_log`, `trace_sql` — what the **running** app actually did | `HW.Api` in-memory buffers | `GET /api/diagnostics/{logs,sql}` |
| The patch approval queue — hashing, staleness, the applier | `IPatchProposalStore` / `IPatchApplier` | `POST /api/dev-agent/patches` |
| Build, test, git, file writes | nowhere in C# | native, in `src/tools/shell.ts` |

Only the fixer gets the trace tools — a failing test that is about runtime behaviour rather than
compilation is exactly where those buffers earn their place.

**One change was made to the C# side**: `POST /api/dev-agent/patches` in `DevAgentController`, so the
loop submits into the existing queue rather than growing a second, competing approval path. It hashes
each file as it currently stands in your workspace, so the existing staleness check still catches a
file edited between proposal and approval.

HW.Api is optional. Without it you lose the trace tools and the shared queue; the plan → code → build
→ test → review cycle is unaffected, and the change still lands on its branch.

## Setup

```bash
cd agentic
npm install
cp .env.example .env       # then put your ANTHROPIC_API_KEY in it
npm run loop -- doctor     # checks key, repo, dotnet, HW.Api, checkpointer
```

`doctor` distinguishes `fail` (the loop cannot run) from `warn` (it runs with less), and exits
non-zero on a failure.

## Use

```bash
# Start a run. It stops at the plan gate.
npm run loop -- run "Add a soft-delete filter to the Vocab queries so deleted rows never surface"

# Read the plan it printed, then one of:
npm run loop -- resume <threadId> --approve
npm run loop -- resume <threadId> --revise "Put the filter in a global query filter, not each handler"
npm run loop -- resume <threadId> --reject "We already do this in the repository layer"

# It then codes, builds, tests and repairs on its own, and stops again at the patch gate.
npm run loop -- diff <threadId>        # read the full diff
npm run loop -- resume <threadId> --approve

# At any time:
npm run loop -- status <threadId>      # where it is, what passed, the timeline, token cost
```

An approved change is left on its branch (`agentic/<id>`) and queued in HW.Api if it was reachable.
Merging is yours: `git merge agentic/<id>`, or approve the patch through the existing
`POST /api/dev-agent/patches/{id}/approve`.

## The agents

Tool lists are narrow on purpose, exactly as in `AgentCatalog.Defaults()`. A reviewer handed a write
tool stops reviewing and starts fixing; a planner handed the whole tree re-explores instead of
planning.

| Role | Tools | Effort | Job |
|---|---|---|---|
| `scout` | `list_files` `read_file` `search_code` | medium | How this part of the codebase works today, and the pattern to follow |
| `planner` | `read_file` `search_code` | high | The structured plan the human approves |
| `coder` | + `write_file` `run_build` | xhigh | Implement the approved plan |
| `fixer` | + `run_tests` `trace_log` `trace_sql` | xhigh | Diagnose a failure from its actual output and fix the cause |
| `reviewer` | read-only | high | Judge a green change against the acceptance criteria and the layering rules |

Every role defaults to `claude-opus-5` with adaptive thinking. Moving a role to `claude-sonnet-5` or
`claude-haiku-4-5` is a deliberate cost decision — make it per role in `config.ts` or
`loop.config.json`, and check the result rather than assuming.

## Configuration

Defaults live in `src/config.ts`. Override with `loop.config.json` in this directory (deep-merged
over the defaults), and override that with environment variables:

```jsonc
{
  "repo": {
    "root": "C:\\Code\\IP\\HW\\HW",
    "buildCommand": "dotnet build HW.slnx --nologo -v quiet",
    "testCommand": "dotnet test HW.slnx --nologo -v quiet",
    "conventions": "…injected verbatim into the planner and coder prompts…"
  },
  "model": { "models": { "scout": "claude-sonnet-5" } },
  "maxRepairAttempts": 4
}
```

`repo.conventions` is the single place where "this is a DDD/CQRS .NET solution with these layering
rules" is stated. Pointing the loop at a different repository is a config change, not a fork:

```bash
LOOP_REPO_ROOT=C:\Code\Other npm run loop -- run "…"
```

## Layout

```
src/
  config.ts              repo + model + budget config, file and env overrides
  state.ts               the graph's typed state, and the Plan / Review schemas
  models.ts              per-role chat model, usage parsing, cost estimate
  graph.ts               nodes, edges, cycles, the two interrupt gates
  checkpoint.ts          SQLite checkpointer
  cli.ts                 run / resume / status / diff / doctor
  agents/
    definitions.ts       the roster: role, tools, iteration budget
    prompts.ts           system prompts, with the failure each line guards against
    runner.ts            ReAct tool loop + structured output, usage accounting
  tools/
    workspace.ts         sandboxed read/write/search — the path containment boundary
    shell.ts             subprocess runner, git worktree lifecycle, diffing
    dotnetApi.ts         HW.Api client (traces + patch queue), degrades if absent
    index.ts             the tool registry the agents draw from
tests/loop.test.ts       sandbox escapes, glob semantics, graph wiring
```

## Tests

```bash
npm test        # 7 tests, no API calls, no cost
npm run typecheck
```

They cover the two things worth catching before a paid run: the workspace containment check (every
path reaching it was written by a model, and `../../..` is a thing models produce), and the graph's
wiring — that `repair` re-enters through a real build, and that nothing reaches apply without the
patch gate.

## Cost

Every run prints its token usage and an estimated dollar cost. A small feature is typically a few
dollars; a run that burns all its repair attempts costs more and delivers less, which is the number
worth watching. If cache-read tokens stay at zero across a run's iterations, the conversation prefix
is being re-paid at full price every turn.

## Known limits

- **The reviewer sees the diff, not the running system.** It cannot catch a change that compiles,
  passes, reviews well, and is wrong against real data. That is what the fixer's trace tools are for,
  and they only work when HW.Api is running.
- **`dotnet test` needs tests to exist.** This solution currently has no test project, so
  `verify_tests` will pass trivially. Point `testCommand` at a real suite before trusting that gate.
- **The patch queue is in-memory on the C# side** and is lost when HW.Api restarts. The branch is
  the durable artifact; the queue is a review convenience.
- **One run at a time per thread.** Concurrent runs get separate worktrees and are fine, but they
  will contend for the same `dotnet build` NuGet caches.

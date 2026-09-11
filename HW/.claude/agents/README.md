# `agents/` — predefined subagents

## Why predefine instead of describing in a prompt (HR-13)

1. **Tool restrictions are a hard constraint.** Without `Edit`/`Write`, `reviewer` **cannot**
   change code, no matter what prompt tries to talk it into doing so. A sentence saying "don't
   edit code" in a prompt cannot make that promise.
2. **Reuse** — the same definition works both for delegation and for a teammate.
3. **Clean context** — a subagent does not carry the main thread's conversation history, and it
   also **does not return** the pile of junk it read.

## Agent table

| Agent | Can write? | Role in the loop | Split out because |
|---|---|---|---|
| `explorer` | ❌ | GATHER | reads 200 files, returns 10 lines — the junk stays on the other side |
| `analyst` | ✅ (`docs/` only) | GATHER | a spec is its own deliverable and needs a lot of reading |
| `architect` | ❌ | PLAN | decides before anyone writes code |
| `implementer` | ✅ | ACT | executes inside an agreed scope |
| `bug-hunter` | ❌ | GATHER | **cognitive independence** — diagnosis separated from cure |
| `verifier` | ❌ | VERIFY | has no stake in declaring "it's done" |
| `reviewer` | ❌ | VERIFY | does not see the author's self-justifying reasoning |
| `reporter` | ✅ (`docs/` only) | REPORT | reads evidence, does not trust narration |

## When **not** to split off a subagent (AG-15, MA-15)

The real reason to split is **context isolation**, not "run it in parallel to go faster". Don't
split when:

- The work needs the **whole conversation history** — a subagent starts cold, and restating the
  task usually costs more than doing it yourself.
- The work is **short and sequential** — reloading project context costs more than it gains.
- The work **edits the same file** — two agents writing one file is a conflict, not parallelism.

> *"Handoff cost is a real cost. If describing the task to a subagent takes more work than doing
> it yourself, don't split."* (MA-6)

## Handoff protocol — six parts (MA-6)

A subagent does **not** see your previous 30 turns. A thin spawn prompt gives off-target results.
Always supply all six parts:

```
1. ROLE + SCOPE        : "Security review of src/auth/. READ ONLY."
2. KNOWN CONTEXT       : "JWT lives in an httpOnly cookie; refresh flow was fixed yesterday."
3. DEFINITION OF DONE  : "Each finding: file:line, exploit scenario, severity."
4. CONSTRAINTS / BANS  : "Do not edit files. Do not run migrations. Do not speculate."
5. EXPECTED OUTPUT     : a concrete format the lead can aggregate
6. KNOWN DEAD ENDS     : "CORS already checked, it is not the cause."
```

Part 6 is the one most often dropped and the most expensive to drop — without it, the subagent
walks the exact path you just walked.

## Scale

3–5 agents on one job is the pragmatic ceiling. Coordination cost grows **O(k²)** in the number of
communication channels, while the parallelism benefit grows **O(k)** and then saturates — there is
always a point where one more agent makes the system **worse** (MA-8). **Three focused beats five
scattered.**

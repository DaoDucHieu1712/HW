---
name: feature-dev
description: Write a new feature or a new function in the HW solution as a proper vertical CQRS slice — lock the scope, put code in the right project, work outward from the domain, test in step, and stop in the right place. Use when the user says "add a feature", "write a function", "add an endpoint", "build this capability", "new code", "add a tool for the agent".
argument-hint: "[feature / function to build]"
---

# Writing a new feature / new function

> **Scope is something you lock first, not something you discover as you go.**
> The most common failure mode at this step is not writing the wrong thing — it is writing **too
> much**.

## 1. Lock the scope before typing a line

Write out these three lines and **have the user confirm them if anything is ambiguous**:

```
DO:        <exactly what>
DON'T:     <the adjacent thing we deliberately leave alone>
DONE WHEN: <which verifier is green>
```

Two readings leading to substantially different work -> ask one question, do not pick blindly and
then write 300 lines.

Still ambiguous at the business level -> write an
[SRS](../../../HW.CS/.claude/skills/spec-srs/SKILL.md) first.
Business is clear but the design is not (API contract, data model) -> write an SRD first.

## 2. Put the code in the right project

| What you are writing | Where it goes |
|---|---|
| Entity, value object, domain event, invariant | `HW.Domain` |
| Command/Query + handler + validator + DTO | `HW.Application/Features/<Feature>/` |
| A tool for the feature's agent | `HW.Application/Features/<Feature>/Agent/Tools/` |
| Loop/tool/provider shared by **every** app | `HW.Agentic` |
| EF config, repository, store, messaging | `HW.Infrastructure` |
| Controller, middleware, startup DI | `HW.Api` |
| Tests | `HW.UnitTests` |

`HW.Agentic` **must not** reference back up into Application/Domain — a test enforces it
(`HW.UnitTests/Agentic/AgenticIsolationTests.cs`). Anything that is only right for one feature
belongs to that feature, even when it is an agent tool.

## 3. Work inside-out, one verify per layer

A vertical slice, in this exact order — run `Verify.ps1 -Level build` before moving to the next step:

```
1. Domain      entity/VO + invariants + domain events  -> test the domain first, no infrastructure needed
2. Application command/query + handler + validator     -> test through VocabTestHarness (or an equivalent harness)
3. Infrastructure  EF config / repository / store      -> a migration is an action that NEEDS APPROVAL
4. Api         controller + route + response shape
5. DI          register in the feature's ServiceCollectionExtensions
```

Going the other way (controller first) almost always leaks DTOs back into the domain.

### Existing shapes to follow

| Need | Read this file as the template |
|---|---|
| Command + validator + handler | `HW.Application/Features/Vocabs/Commands/CreateVocab/CreateVocabCommand.cs` |
| Query with paging + filtering | `HW.Application/Features/Vocabs/Queries/GetVocabs/GetVocabsQuery.cs` |
| Entity with invariants + domain events | `HW.Domain/Entities/Vocab.cs` |
| Value object | `HW.Domain/ValueObjects/Word.cs` |
| Agent tool going through `ISender` | `HW.Application/Features/Vocabs/Agent/Tools/SaveVocabTool.cs` |
| A feature's DI | `HW.Application/Features/Vocabs/Agent/VocabAgentServiceCollectionExtensions.cs` |

Read a template **before** writing. New code must read like the code around it: same comment
density, same naming, same idioms.

## 4. Three laws while writing

1. **Invariants belong to the domain, not to the handler.** The handler orchestrates; the entity is
   where "not allowed" is said. Validators reject junk input; they do not replace invariants.
2. **Queries do not open transactions.** Long-running work (agents, fan-out) makes this more
   important, not less — see the note in `AskVocabAgentQuery.cs` for why.
3. **No abstraction for a single call site.** A second interface appears when there is a real second
   implementation.

## 5. Test in step, not at the end

Write tests **immediately after each layer**, not all at the end — see
[unit-test](../unit-test/SKILL.md). Minimum for a new feature:

- domain: invariants + each domain event;
- handler: happy path + not-found + one boundary case;
- validator: one test per rule;
- tool (if any): the parameters actually sent + the message when the result is empty.

## 6. Stop in the right place

Done = `Verify.ps1 -Level full` green **and** nothing outside the `DO:` line was touched.

Spot another problem along the way: **write it down, do not fix it.** Put it under "Seen but not
touched" in the report. That is the line between a change that can be reviewed and a change that
takes an afternoon to read.

Need an irreversible action (migration, broker schema change, deleting data) -> **stop and ask for
approval**, do not do it and report afterwards.

## Report

```
## Done
DO: <the exact line agreed in §1>

## Changes by layer
- Domain: …
- Application: …
- Infrastructure: …
- Api: …

## Tests
<n> new tests in `HW.UnitTests/<path>` — covering: <invariants / handler / validator / tool>

## Evidence
`Verify.ps1 -Level full` -> PASS (<n> passed, 0 failed)

## Seen but not touched
- <problem>, `file.cs:line` — <why it was left>

## Needs approval
- <migration / irreversible action>, or: none
```

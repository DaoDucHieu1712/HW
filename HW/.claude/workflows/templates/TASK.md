# Current task

> Copy this file to `.claude/state/CURRENT_TASK.md` and fill it in.
> The `SessionStart` hook loads it into context every session — meaning you **do not have to restate
> the task** each time you open Claude Code, and a cold-starting subagent can read it too.

---

## Goal

<One sentence. If it will not fit in one sentence, the task is too big — split it.>

## Definition of "done" (required — must be verifiable)

- [ ] <condition 1> — verified by: `<command>`
- [ ] <condition 2> — verified by: `<command>`

> With no verification command, the agent decides for itself when it is "done" — and it will decide
> early (AG-8, stopping short).

## Scope

**May change:**
- `path/…`

**May NOT change:**
- `path/…`

## Known context

- <what has been tried / what is known, so the agent does not walk the old path again>

## Known dead ends

- <what was checked and ruled out — the most often skipped and most expensive part, MA-6 part 6>

## Constraints

- <do not change the public API / no new dependencies / must stay backwards compatible / …>

## Budget

| | |
|---|---|
| Turn ceiling | 25 |
| Human approval needed before | <migration / push / calling an external API> |

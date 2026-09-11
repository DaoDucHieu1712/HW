---
description: Run the disciplined bug-fixing procedure — reproduce, competing hypotheses, red test first, minimal fix
argument-hint: "[symptom description or error message]"
---

## Current build status

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Verify.ps1 -Level build`

## Recent changes (the first suspects)

!`git log --oneline -8`
!`git diff --stat HEAD~1`

## Symptom

$ARGUMENTS

## Task

Follow the `fixbug` skill. **Do not skip steps.**

1. **Reproduce** first. Cannot reproduce -> say so plainly; everything after that is speculation and
   must be labelled as such. Only have logs and no repro yet -> run `/trace-log` first.
2. **Read the original error message in full** — the whole stack trace, not just the first line.
   Note: `MSB3021`/`MSB3027` means a running `HW.Api` is locking the DLL, **not** a code error.
3. **Build at least 3 competing hypotheses** before digging into any of them. For each one, go
   looking for **disconfirming evidence**, not supporting evidence.
   Hard bug / needs a wide sweep -> delegate to the `bug-hunter` agent (read-only, must prove it
   first).
4. **Write a red test that reproduces the bug — before fixing.** Put it in `HW.UnitTests` at the
   right seam (handler -> `VocabTestHarness`, tool -> `StubSender`). The test must fail **now**, for
   exactly the reason the bug causes. If it is green right away, you do not understand the bug yet.
5. **Minimal fix** — fix the cause, not the symptom. No drive-by refactor. Spot another problem ->
   write it down, do not fix it.
6. **Verify**: `Verify.ps1 -Level full`. The new test green **and** no additional old tests red.

## Stop if

The same fix fails **3 times** — that is thrashing, not perseverance. Pick one:
change hypothesis · narrow the repro · stop and report (what was tried, how it failed, what is
needed).

Needs a migration or any irreversible operation -> **ask for approval**, do not do it and report
afterwards.

## Final report

Root cause at `file:line` · the minimal change · evidence (test red -> green) ·
**other places in the repo with the same defect** (list them, do not fix them).

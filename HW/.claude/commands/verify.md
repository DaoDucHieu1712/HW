---
description: Run the full verifier (build + test) and report the state objectively
---

## Verifier result

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Verify.ps1 -Level full`

## Changes currently on disk

!`git status --short`
!`git diff --stat`

## Task

Read the result **above** — it has already run, do not run it again.

Report in **three states**, not two:

| Result | Means |
|---|---|
| **PASS** | the verifier ran and was green |
| **FAIL** | the verifier ran and was red |
| **CANNOT VERIFY** | the verifier could not run — the state is **unknown** |

Blurring the third state into PASS is the most common way a loop deceives itself.

If **FAIL**: quote the error message **verbatim** with `file:line`, then state a hypothesis about
the cause — but do not fix it until asked.

If **PASS**: say so plainly, without hedging. And name **which part of the change has no test
coverage** — green is not the same as verified.

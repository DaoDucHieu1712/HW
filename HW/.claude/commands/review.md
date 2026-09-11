---
description: Review the changes on the branch — correctness and security, read only
argument-hint: "[scope, default: the branch diff]"
---

## Diff to review

!`git diff --stat`
!`git diff`

## Additional scope

$ARGUMENTS

## Task

Delegate to the `reviewer` agent (it has **no** `Edit`/`Write` — deliberately: a reviewer who can
change code is a reviewer who has lost their independence).

Review only the **changes on the branch**, not the whole repo. Pre-existing technical debt is not a
finding of this review.

Ranking: **BLOCKER** (correctness bug / vulnerability with an exploit scenario) · **MAJOR** (will
break in production under foreseeable conditions) · **MINOR** (correct but hard to maintain) ·
**NIT** (at most 3).

Three hard rules:
1. Every finding has a `file:line` **and** a concrete failure scenario: which input -> which state
   -> which wrong result. No evidence-free speculation.
2. Do not invent requirements. "Should add caching" is not a review finding unless there is evidence
   of a real performance problem.
3. **"Nothing to report" is a valid result.** Do not manufacture findings to look useful.

End with a **"Checked, no issues"** section so the reader knows how far the review reached.

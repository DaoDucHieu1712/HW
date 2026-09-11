---
name: fixbug
description: The disciplined bug-fixing procedure for the HW solution — reproduce, build competing hypotheses, hunt for disconfirming evidence, write a red test first, make the minimal fix, verify. Use when the user reports an error, an exception, wrong behaviour, a red test, or says "fix", "bug", "why is this failing", "it doesn't work".
argument-hint: "[symptom description]"
---

# The bug-fixing procedure

> **A fix without a red-first test is a fix you cannot prove.**
> You do not know whether you fixed the bug or merely hid it.

## Six steps — skip none of them

### 1. Reproduce
Get the failure to happen first. Cannot reproduce -> **say so plainly**, and label every conclusion
after that as speculation. Do not blind-fix a failure you have never seen.

Only have logs/a stack trace and no repro -> run
[trace-error-log](../trace-error-log/SKILL.md) first to build the timeline, then come back here.

### 2. Read the **original** error message, in full
The whole stack trace, not the first line. The first line says *where it blew up*; the stack says
*how it got there*.

In this repo specifically, two kinds of error are routinely misread as code errors:

| Code | Actually means |
|---|---|
| `MSB3021` / `MSB3027` | `HW.Api` is running and locking the DLL — kill the process, this is not a code fix |
| `CS0246` after a namespace move | the file lost the `using` for the old parent namespace — add the `using`, it is not a missing package |

### 3. Build **at least 3 competing hypotheses** before digging into any of them
One hypothesis = nothing to compare against = you will find exactly what you already believed
(anchoring).

```
| # | Hypothesis | If true, what ELSE must be true? | How to check it |
```

Column 3 is the working column. For each hypothesis, go looking for **disconfirming evidence**, not
for supporting evidence.

Hard bug, many layers, or needs a wide sweep -> delegate to the `bug-hunter` agent (read-only, forced
to prove it before anyone touches the code).

### 4. Write a **red** test that reproduces the bug — before fixing
This test must **fail** right now, for exactly the reason the bug causes. If it is green immediately,
you do not understand the bug yet.

Put it in `HW.UnitTests`, using the existing harnesses — see [unit-test](../unit-test/SKILL.md) §1
for choosing the seam. A bug in a handler is tested through `VocabTestHarness`, a bug in a tool
through `StubSender`; a test at the wrong layer stays green both before and after the fix.

```powershell
dotnet test HW.UnitTests/HW.UnitTests.csproj --filter "FullyQualifiedName~<TestName>" --nologo
```

### 5. Make the **minimal** fix
- Fix the **cause**, not the symptom. Wrapping the blow-up site in `try/catch` hides the bug.
- **No** drive-by refactor. No tidying the surrounding code. One fix = one understandable change.
- Spot another problem -> write it down, do not fix it.

### 6. Verify
```powershell
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level full
```
The new test **green** *and* no additional old tests **red**. Both, not just the first.

## When stuck

The same fix failing **3 times** => you are thrashing, not persevering.
The `loop-progress.ps1` hook will stop you. At that point, pick **one**:

1. **Change hypothesis** — back to step 3: which hypothesis has not been rejected?
2. **Narrow it down** — reproduce with the smallest possible test; eliminate variables.
3. **Stop and report** — what was tried, how it failed, what decision is needed.

## Report

```
## Root cause
`file.cs:line` — <mechanism, explaining why the symptom looks exactly as observed>

## Fixed
<minimal change>

## Evidence
- Test `<name>`: RED before the fix -> GREEN after
- `Verify.ps1 -Level full` -> PASS

## Other places with the same defect (not fixed)
- …
```

That last section matters: a bug is rarely in only one place.

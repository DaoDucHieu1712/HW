---
description: Write one new function/method inside existing code — match the surrounding shape, narrow scope, tests included
argument-hint: "[the function to write, and where]"
---

## Function to write

$ARGUMENTS

## Task

The narrow version of the `feature-dev` skill: **one function, one place, no spreading**.

1. **Read the whole target file** before adding anything. The new function must read like its
   neighbours — same naming, same comment density, same idioms. Comments explain **why**, they do
   not narrate the code.
2. **Put it at the right layer.** A business constraint ("must not be allowed to…") belongs to the
   entity in `HW.Domain`, not to a handler. Handlers orchestrate. Validators reject junk input;
   they do not replace invariants.
3. **No abstraction for a single call site.** A second interface appears when there is a real
   second implementation.
4. **Handle the boundaries inside the function**: null, empty, 0, max, invalid state. Do not
   swallow exceptions — throw a real error whose message says what is wrong and what a valid value
   would be.
5. **Write the tests in the same pass**, in `HW.UnitTests`: happy path + boundary + error case.
   See the `unit-test` skill for choosing the seam.
6. **Verify**: `Verify.ps1 -Level full`.

## Forbidden

- No refactoring of the surrounding code. Spot another problem -> write it down, do not fix it.
- No changing a public signature that has callers without saying so first.
- No new package without saying so first.

## Final report

`file.cs:line` of the new function · why that layer · boundary cases handled · tests included ·
`Verify.ps1` PASS · seen but not touched.

---
description: Generate unit tests for existing code — pick the seam, cover the four case groups, prove the tests catch defects
argument-hint: "[class / feature / file to test]"
---

## Build status

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Verify.ps1 -Level build`

## Existing tests

!`git ls-files HW.UnitTests --exclude-standard`

## To test

$ARGUMENTS

## Task

Follow the `unit-test` skill. **Do not skip steps.**

1. **Read the harness before writing.** `HW.UnitTests/TestSupport/` already has
   `VocabTestHarness`, `VocabBuilder`, `StubSender`, `Json`. Reuse them. There is no
   Moq/FluentAssertions in the project — plain xUnit asserts and hand-written stubs.
2. **Read the nearest existing test file** to match its shape before typing the first line.
3. **Pick the seam**: entity -> call it directly · handler -> `VocabTestHarness` · validator ->
   `Validate()` · agent tool -> `StubSender`. Test through the same door production goes through.
4. **List the cases before writing**, all four groups: happy path · boundary · error ·
   **preservation**. The "preservation" group is the most often skipped and catches the most bugs.
5. **Write the tests.** One behaviour per test. The test name is a claim about behaviour. Never
   assert on a random value — assert invariants, or aggregate over several runs.
6. **Prove the tests catch defects**: break the production code in one place -> the test goes RED
   for the right reason -> revert -> GREEN. If you cannot, say so plainly rather than staying quiet.
7. **Verify**: `Verify.ps1 -Level full`. Randomness involved -> re-run 3 times.

A large batch of tests, or tests spanning several features -> delegate to the `test-writer` agent
(writes only inside `HW.UnitTests`, must not change production code).

## Forbidden

**Do not change production code to make a test pass.** A test that is red because the code is wrong
is a finding — record it under "Red because the code is wrong" and hand it to `/fixbug`.

## Final report

Test count and paths · the four-group coverage table · evidence the tests catch defects
(red -> green) · the `dotnet test` result · **areas deliberately left untested and why**.

---
name: unit-test
description: Generate unit tests for existing code in the HW solution — pick the right seam, use the harnesses that exist (VocabTestHarness, StubSender, builders), write tests against behaviour rather than lines of code, and prove the tests really catch defects. Use when the user says "write tests", "unit test", "gen test", "raise coverage", "test this function", "test case".
argument-hint: "[class / feature / file to test]"
---

# Generating unit tests — the procedure

> **A test that can never go red proves nothing.**
> Coverage measures which lines *ran*, not which behaviour was *asserted*.

All tests live in `HW.UnitTests` and run with:

```powershell
dotnet test HW.UnitTests/HW.UnitTests.csproj --nologo -v q
```

## 1. Before writing: read the existing harnesses

This repo already has test infrastructure — **reuse it, do not build new**:

| Need | Use | Where |
|---|---|---|
| A real DB for a handler (`ToListAsync`, query filters, value converters) | `VocabTestHarness` | `HW.UnitTests/TestSupport/VocabTestHarness.cs` |
| Building an entity in any state | `VocabBuilder` | `HW.UnitTests/TestSupport/VocabBuilder.cs` |
| Faking MediatR for an agent tool | `StubSender` | `HW.UnitTests/TestSupport/StubSender.cs` |
| A tool's JSON parameters | `Json.Args(...)` | `HW.UnitTests/TestSupport/Json.cs` |

There is no mocking library in this project (no Moq/NSubstitute/FluentAssertions). Use **plain xUnit
asserts** and hand-written stubs. Feeling that you need a mock almost always means you are testing
at the wrong seam.

No harness yet for the area you are testing (say `Notes`, `Blogs`, `Sagas`) -> write a new harness
**following the exact shape** of `VocabTestHarness`, put it next to it in `TestSupport/`, and only
then write the tests.

## 2. Pick the seam — this decision matters more than the number of tests

| Subject | How to test it | Existing example |
|---|---|---|
| Entity / value object | call it directly, no infrastructure | `Domain/VocabTests.cs`, `Domain/WordTests.cs` |
| Command / Query handler | `VocabTestHarness` + the real repository over EF InMemory | `Application/Queries/GetVocabsQueryHandlerTests.cs` |
| Validator (FluentValidation) | `validator.Validate(x).IsValid` — no harness needed | at the end of each handler test file |
| Agent tool | `StubSender` — assert on **the request sent**, not on the DB | `Application/Agent/VocabAgentToolTests.cs` |
| Architectural constraint | reflection over the assembly | `Agentic/AgenticIsolationTests.cs` |

The rule: **test through the door production goes through**. Handlers go through `IRepository`, so
the tests do too. Tools go through `ISender`, so the tests assert on `ISender`.

## 3. List the cases before typing any code

For each unit, walk all four groups — if a group is missing, say it was deliberate:

```
| Group        | Asks                                                    |
|--------------|---------------------------------------------------------|
| Happy path   | valid input -> correct result                            |
| Boundary     | 0, 1, max, max+1, empty, null, whitespace-only string    |
| Error        | not found, wrong state, invariant violation              |
| Preservation | does what must NOT change actually stay unchanged?       |
```

Group 4 is the one most often skipped and it catches the most bugs: "does editing the content break
the review schedule?", "does soft-deleting one record affect another?".

## 4. Write the tests

**A test name is a claim about behaviour**, not the name of a method:

```csharp
// good
public async Task Treats_a_soft_deleted_word_as_gone()
public void A_mastered_word_cannot_be_reviewed_again()

// bad — restates the signature, says nothing about behaviour
public void TestMarkReviewed()
public void MarkReviewed_Should_Work()
```

Three laws:

1. **One behaviour per test.** Needing two `Assert`s for two different reasons -> split into two
   tests.
2. **The setup must not be the thing under test.** `VocabBuilder` calls `ClearDomainEvents()` at the
   end for exactly this reason: the event a test asserts on must have been raised by the code under
   test.
3. **Never assert on something random.** Wherever `Random`/`Guid`/`DateTimeOffset.UtcNow` is
   involved, assert an **invariant** (count, set membership, range) instead of a specific value. To
   be sure that "at least one of kind X" appears across random draws, aggregate several runs — see
   `ManyDrawsAsync` in `GenerateVocabExamQueryHandlerTests.cs`.

## 5. Prove the tests catch defects

This is the step most often skipped, and skipping it makes the whole suite decoration.

For **at least one** important test in the batch you just wrote:

1. Break the production code in exactly one place (invert a condition, drop a filter);
2. Run the test -> it must be **RED**, and red for the right reason;
3. Revert the production code;
4. Run again -> **GREEN**.

If you cannot do this step, say so plainly in the report rather than staying quiet.

## 6. Verify the whole batch

```powershell
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level full
```

Tests involving randomness -> re-run **3 times** to weed out flakiness. A test red 1 time in 3 is a
broken test, not "bad luck".

## Report

```
## Tested
<class / feature>, <n> tests in `HW.UnitTests/<path>`

## Coverage by group
- Happy path: …
- Boundary: …
- Error: …
- Preservation: …

## Evidence the tests catch defects
Broke `file.cs:line` (<what changed>) -> `<test name>` RED -> reverted -> GREEN

## Result
`dotnet test` -> <n> passed, 0 failed (ran <k> times, stable)

## Deliberately NOT tested
- <what> — <why>
```

That last section is the most honest one. An untested area that goes unmentioned will be read as a
tested area.

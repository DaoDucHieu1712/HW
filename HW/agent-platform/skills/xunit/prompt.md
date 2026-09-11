Test behaviour, not lines. A test that only re-states the implementation passes
forever and catches nothing.

**Pick the seam first.** Test the public entry point of the unit -- the handler,
the aggregate method, the parser. Reaching for `internal` or reflection is a
sign the seam is wrong, not that the test needs more access.

**Cover four groups, in this order**

1. The happy path -- the behaviour someone actually wanted.
2. Boundaries -- empty, one, many; first, last; min, max; just over the limit.
3. Failure modes the code really has -- the exceptions it throws, the results it
   returns, the state it refuses to enter.
4. Regressions -- one test per fixed bug, named after the bug.

**Mechanics**

- `[Fact]` for one case, `[Theory]` with `[InlineData]` for a table. Do not write
  a `foreach` inside a `[Fact]`.
- Arrange / Act / Assert, with a blank line between the three.
- Name the test after the behaviour: `Cancel_AfterShipping_ReturnsFailure`.
- Assert on the outcome, not on how it was reached. Verifying a mock call is a
  last resort, for when the call *is* the observable effect.
- Use the fixtures and builders the repository already has before writing new
  ones.

**Before claiming a test is good, break the code it covers.** If the test still
passes, it is testing nothing.

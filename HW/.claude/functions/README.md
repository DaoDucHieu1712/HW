# `functions/` — the shared deterministic library

These three scripts are the **non-probabilistic part** of the loop. They are called from three
places:

1. **Hooks** — `stop-quality-gate.ps1` runs the same logic as `Verify.ps1`;
2. **Slash commands** — through the `` !`command` `` syntax, so the result is **already in the
   prompt** instead of making the model go and call a tool (HR-9);
3. **Humans** — run by hand when you want to check something.

| Script | Answers | Exit code |
|---|---|---|
| `Verify.ps1` | *"Is it actually done?"* | `0` = pass, `1` = fail |
| `Task.ps1` | *"Which step is the loop on, how much budget is left?"* | `2` = out of budget |
| `Trace.ps1` | *"Where is the loop broken?"* | `0` |

---

## Why `` !`cmd` `` matters more than it looks

```markdown
## Verifier result
!`powershell -NoProfile -File .claude/functions/Verify.ps1 -Level build`
```

Without it, the model must **decide for itself** to call a tool to get the build result — one extra
turn, and it **might forget**. With it, the data is already in the prompt before the model thinks its
first thought.

This is the **workflow (deterministic)** vs **agent (probabilistic)** boundary applied at the level
of a single command.

---

## Extending it for your repo

`Verify.ps1` only implements the **tier A (build)** and **tier B (test)** verifiers. Tier C —
*asserting against real state* — is something only your repo can write, and it is the tier most worth
investing in for a business agent:

```powershell
# Add to Verify.ps1, after the 'test' stage:
if ($Level -eq 'full' -and $env:LOOP_ASSERT_CMD) {
    $results.Add((Run-Stage -Name 'assert' -Grade 'C' -Command $env:LOOP_ASSERT_CMD))
}
```

Tier C examples: call `GET /blogs/{id}` again after the agent says it created the blog; re-query the
`OutboxMessages` table to confirm the domain event was written. **An agent saying "created" is not
evidence; being able to read it back is.**

Switch the commands for another language through environment variables, with no script edits:

```jsonc
// .claude/settings.json
"env": {
  "LOOP_BUILD_CMD": "npm run typecheck",
  "LOOP_TEST_CMD":  "npm test -- --run",
  "LOOP_ASSERT_CMD": "npm run smoke"
}
```

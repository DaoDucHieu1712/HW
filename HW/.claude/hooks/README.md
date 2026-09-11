# Hooks — the loop's **deterministic** layer

> **The founding claim (AI-05, HR-1):** the model is the **probabilistic** part; the harness is the
> **deterministic** part.
> Anything you want to happen **100% of the time** — formatting after an edit, blocking `rm -rf`, not
> letting a turn end while the build is red — **must not** go in a prompt. It goes in a
> **hook / permission**.
>
> The first question when adding anything: **"is it acceptable for this not to happen?"**
> Must never be missed -> hook. Merely *should* happen -> a skill or CLAUDE.md.

---

## 1. The hooks in this template

| Script | Event | Matcher | Blocks? | Role in the loop |
|---|---|---|---|---|
| `session-start-context.ps1` | `SessionStart` | `startup\|resume\|clear` | ❌ | **GATHER** — loads branch/diff/task into context from real data |
| `prompt-guard.ps1` | `UserPromptSubmit` | — | ❌ | labels untrusted data; resets the thrashing counter |
| `block-dangerous.ps1` | `PreToolUse` | `Bash` | ✅ **deny/ask** | a safety boundary no prompt can talk around |
| `protect-secrets.ps1` | `PreToolUse` | `Read\|Grep\|Glob` | ✅ **deny** | blocks reading secrets |
| `format-after-edit.ps1` | `PostToolUse` | `Edit\|Write` | ❌ (async) | **ACT** — enforces formatting 100% of the time |
| `loop-progress.ps1` | `PostToolUseFailure` | `*` | ⚠️ exit 2 | **STOP** — detects thrashing (AG-6 §4) |
| `stop-quality-gate.ps1` | `Stop` | — | ✅ **exit 2** | **VERIFY** — guards against "stopping short" (AG-8) |
| `subagent-stop-log.ps1` | `SubagentStop` | — | ❌ (async) | multi-agent economics data (MA-2) |
| `pre-compact-snapshot.ps1` | `PreCompact` | `auto\|manual` | ❌ (async) | keeps a trace before the context is compacted |
| `audit-log.ps1` | `PostToolUse` | `*` | ❌ (async) | audit -> `.claude/state/audit.jsonl` |

POSIX versions of the four most important hooks live in `sh/` (macOS/Linux, needs `jq`).

---

## 2. The I/O contract — this table is all you need (HR-5)

**In:** JSON on **stdin**. Common fields: `session_id`, `transcript_path`, `cwd`,
`permission_mode`, `hook_event_name`, `effort`; tool events also carry `tool_name`, `tool_input`,
`tool_use_id`.

**Out — three routes:**

| Exit code | Meaning |
|---|---|
| **0** | Success. stdout only goes to the debug log — **except** for `UserPromptSubmit`, `UserPromptExpansion`, `SessionStart`, `PostModelSwitch`, where stdout **is fed into the model's context** |
| **2** | **Blocking error.** `PreToolUse` blocks the tool · `UserPromptSubmit` rejects the prompt · `Stop`/`SubagentStop` **refuse to stop** · `TaskCompleted` refuses the done marker. The message comes from **stderr** |
| anything else | Non-blocking error — logged, the agent continues |

**Or print JSON on stdout for structured control:**

```json
{
  "systemMessage": "shown in the transcript",
  "additionalContext": "added to Claude's context",
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "permissionDecision": "allow | deny | request",
    "permissionDecisionReason": "the reason — Claude can read it",
    "updatedInput": { "…": "REWRITE the tool arguments before it runs" }
  }
}
```

⚠️ **Two details people always ask about:**
- `PostToolUse` **cannot prevent** a tool (it already ran). exit 2 there only **hands stderr to
  Claude** so it can correct itself — that is exactly how `loop-progress.ps1` redirects a stuck
  agent.
- `updatedInput` lets a hook **rewrite the tool arguments**. Very powerful, but the model **does not
  see** that it was rewritten — use it sparingly.

---

## 3. "Function hooks" — five **kinds** of hook, not just `command`

The `type` field in `hooks[]` decides *who* performs the check. This is the most overlooked design
axis:

| `type` | Who runs it | Deterministic? | Used for | Example in this template |
|---|---|---|---|---|
| `command` | your shell | ✅ | the default — fast, cheap, verifiable | every hook above |
| `http` | a remote endpoint | ✅ | central audit/compliance across many repos | see §3.1 |
| `mcp_tool` | an MCP tool | ✅ | policy lookups in an internal system | see §3.2 |
| `prompt` | **the model itself** | ❌ | semantic judgement a regex cannot make | see §3.3 |
| `agent` | a subagent | ❌ | multi-step assessment (a security review before push) | see §3.4 |

> ⚖️ **The trade-off must be said out loud:** `prompt` and `agent` give up **the very reason hooks
> exist** — determinism. They are **tier D** verifiers on the AG-5 ladder. Use them if and only if
> there is no tier A–C verifier (compiler / test / state assertion). And they cost tokens **every
> time the event fires**, so do not attach them to the `PreToolUse` of a frequently called tool.

### 3.1 `http` — central audit

```json
{ "PostToolUse": [{ "matcher": "Edit|Write|Bash",
  "hooks": [{ "type": "http",
              "url": "https://compliance.internal/api/claude-audit",
              "headers": { "Authorization": "Bearer ${COMPLIANCE_TOKEN}" },
              "timeout": 10, "async": true }] }] }
```
Secrets travel through `${ENV}`, **never** hard-coded into a committed file.

### 3.2 `mcp_tool` — ask the internal policy system

```json
{ "PreToolUse": [{ "matcher": "Bash",
  "hooks": [{ "type": "mcp_tool",
              "server": "policy",
              "tool": "check_command",
              "timeout": 15 }] }] }
```

### 3.3 `prompt` — semantic judgement

```json
{ "Stop": [{ "hooks": [{ "type": "prompt", "model": "claude-haiku-4-5-20251001",
  "prompt": "Read the transcript. Did the agent declare completion WITHOUT running any verifier? If so, return JSON {\"block\":true,\"reason\":\"...\"}. Otherwise, {\"block\":false}." }] }] }
```
Use the cheapest model that can do the gatekeeping — it runs **every turn**.

### 3.4 `agent` — multi-step assessment

```json
{ "PreToolUse": [{ "matcher": "Bash", "if": "Bash(git push:*)",
  "hooks": [{ "type": "agent", "agent": "reviewer", "timeout": 300 }] }] }
```

---

## 4. The security of hooks themselves (HR-8)

Hooks run **as the user, unsandboxed**. Three consequences:

1. A malicious `.claude/settings.json` in a cloned repo **is executable code**. Review a strange
   repo's settings file the way you would review a `postinstall` script.
2. A hook receives `tool_input` generated by **the model** => the content may be attacker-controlled
   via prompt injection. **Always parse with `jq`/`ConvertFrom-Json`, always quote variables, never
   `eval` / `Invoke-Expression` a command string.**
3. An organisation that wants its hooks to stay in force => put them in **managed settings**, and
   consider controlling the `disableAllHooks` flag.

---

## 5. Operating them

- A hook must be **fast, and silent when it has nothing to do**. A slow hook on `PreToolUse` is
  multiplied by **every** tool call and ruins the experience. Long work => `async: true`.
- **Always** build paths from `${CLAUDE_PROJECT_DIR}` (or `${CLAUDE_PLUGIN_ROOT}` inside a plugin).
  Hard-coded paths are the most common works-on-my-machine failure.
- Debugging: `claude --debug` prints which hook matched, its exit code and its output.
- *"My hook does not run"* is usually: the wrong matcher (it matches the **tool name**, not the
  command text), a file without the execute bit, or a path that does not use
  `${CLAUDE_PROJECT_DIR}`.

### Quick switches

| Variable | Default | Effect |
|---|---|---|
| `LOOP_QUALITY_GATE` | `0` | `1` = enable the build+test gate on `Stop` |
| `LOOP_MAX_SAME_ERROR` | `3` | identical failures allowed before a change of direction is forced |
| `LOOP_AUDIT` | `1` | `0` = stop writing `audit.jsonl` |
| `LOOP_BUILD_CMD` / `LOOP_TEST_CMD` | `dotnet build` / `dotnet test` | the repo's verifier commands |

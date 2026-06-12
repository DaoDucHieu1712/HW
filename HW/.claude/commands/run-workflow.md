# Run Workflow

Execute the full task pipeline: read → analyze → code → test → report → apply.

Read `tasks/CURRENT_TASK.md` to load the current task, then follow this pipeline:

**Step 1 — Read task**
Parse `tasks/CURRENT_TASK.md`. Validate it's filled in (not placeholder). Read the "Affected Areas" files to build context. Write task summary to `tasks/outputs/analysis.md`.

**Step 2 — Analyze**
Load the relevant skills from `.claude/skills/` based on the task type. Read all reference files in the affected layers. Append a full implementation plan (file list, class names, endpoints, validation rules) to `tasks/outputs/analysis.md`.

**Step 3 — Code**
Generate every file in the plan following `CLAUDE.md` rules and skill patterns. Write complete file content (creates) and exact edits (modifies) to `tasks/outputs/code-changes.md`. Run self-check: usings, interface completeness, soft-delete, partial update, auth.

**Step 4 — Test (static review)**
Check each acceptance criterion from `tasks/CURRENT_TASK.md` against the generated code. Check static issues: missing usings, DI mismatches, null risks. Write results to `tasks/outputs/test-results.md`. If blocking issues found, STOP and report — do not apply.

**Step 5 — Report**
Read all outputs. Write `tasks/outputs/report.md` with: summary, file list, API surface, criteria status, post-apply steps. Print report inline.

**Step 6 — Apply**
Print all generated file content. Ask: "Apply to disk? (yes/no)". If yes: write/edit files, run `dotnet build HW.slnx`, report result. Print migration commands.

---

Pass `--skip-apply` to stop after Step 5 (preview only).

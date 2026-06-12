# Prompt: run-workflow

You are an autonomous coding agent for a .NET 8 Clean Architecture project.

## Step 1 — Load context
Read these files in order:
1. `CLAUDE.md` — project rules and conventions
2. `tasks/current-task.md` — the task to implement
3. `.claude/guidelines/layer-boundaries.md`
4. `.claude/guidelines/coding-style.md`
5. `.claude/guidelines/error-handling.md`
6. `.claude/guidelines/api-design.md`

## Step 2 — Load relevant patterns
Based on the task type, read the relevant pattern files from `.claude/patterns/`:
- For any feature task: read all 8 patterns
- For bugfix: read patterns 01 (exceptions), 02 (repository), 05 (api-response)
- For refactor: read patterns 02, 04, 07

## Step 3 — Load agent
Read `.claude/agents/add-feature.md` and follow it completely for the entity described in `tasks/current-task.md`.

## Step 4 — Read reference files
Before generating code, read:
- `HW.Domain/Entities/Blog.cs`
- `HW.Application/Services/BlogService.cs`
- `HW.Application/Dtos/BlogDtos.cs`
- `HW.Application/Validators/BlogValidator.cs`
- `HW.Api/Controllers/BlogController.cs`
- `HW.Infrastructure/ApplicationDbContext.cs`
- `HW.Application/DI/ServiceCollectionExtensions.cs`

## Step 5 — Generate and apply
Generate all code following the agent instructions. Apply every file to disk using Write/Edit tools.
Do NOT ask for confirmation — apply automatically.

## Step 6 — Build
Run:
```bash
cd /c/Code/IP/HW/HW && dotnet build HW.slnx 2>&1
```

## Step 7 — Report
Print a summary:
```
TASK: <title>
STATUS: done | failed

FILES CREATED:
  + path/to/file.cs
  ...

FILES MODIFIED:
  ~ path/to/file.cs
  ...

BUILD: pass (0 errors) | FAIL (N errors)
  <list errors if any>

NEXT:
  dotnet ef migrations add <Name> --project HW.Infrastructure --startup-project HW.Api
  dotnet ef database update --project HW.Infrastructure --startup-project HW.Api
```

Update `tasks/current-task.md` frontmatter `status` to `done` (or `failed` if build failed).

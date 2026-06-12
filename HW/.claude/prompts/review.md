# Prompt: review

You are a code reviewer for a .NET 8 Clean Architecture project.

## Step 1 — Load rules
Read:
1. `CLAUDE.md`
2. `.claude/guidelines/layer-boundaries.md`
3. `.claude/guidelines/coding-style.md`
4. `.claude/guidelines/error-handling.md`
5. `.claude/guidelines/api-design.md`
6. All 8 files in `.claude/patterns/`

## Step 2 — Identify changed files
Run:
```bash
git diff --name-only HEAD
git status --short
```

## Step 3 — Review each changed file
For each changed `.cs` file, read it and check against:

### Layer boundary violations
- DbContext used outside Infrastructure?
- Entity returned from service (not DTO)?
- Business logic in controller?

### Pattern violations
- Mutation without `_uow.ExecuteAsync()`?
- Hard delete on auditable entity?
- Partial update overwrites null fields?
- `FindAll()` used for mutation (no tracking)?
- Missing `IsDelete != true` filter on queries?

### Code style issues
- Non-nullable string without `= string.Empty`?
- Sync method in service (missing `async Task`)?
- `try/catch` in controller?

### API design issues
- Missing `[Authorize]` on POST/PUT/DELETE?
- Missing `ApiResponseFactory` wrapper?
- Verb in route name?

## Step 4 — Output report

```
REVIEW REPORT
=============

Changed files: N
Issues found: N

CRITICAL (must fix before merge):
  HW.Api/Controllers/X.cs:42  — Missing [Authorize] on DELETE endpoint
  HW.Application/Services/X.cs:18 — Mutation outside UoW.ExecuteAsync

WARNING (should fix):
  HW.Domain/Entities/X.cs:7 — Non-nullable string without = string.Empty

OK:
  HW.Infrastructure/ApplicationDbContext.cs ✓
  HW.Application/DI/ServiceCollectionExtensions.cs ✓

VERDICT: FAIL (N critical) | WARN (0 critical, N warnings) | PASS
```

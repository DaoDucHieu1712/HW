# Prompt: add-feature

You are an autonomous coding agent for a .NET 8 Clean Architecture project.
Entity to generate: %%ENTITY%%

## Step 1 — Load context
Read:
1. `CLAUDE.md`
2. `.claude/agents/add-feature.md` — follow this completely
3. All 8 files in `.claude/patterns/`
4. `.claude/guidelines/coding-style.md`
5. `.claude/guidelines/error-handling.md`

## Step 2 — Read reference files
Read these for exact style to match:
- `HW.Domain/Entities/Blog.cs`
- `HW.Application/Services/BlogService.cs`
- `HW.Application/Dtos/BlogDtos.cs`
- `HW.Application/Validators/BlogValidator.cs`
- `HW.Api/Controllers/BlogController.cs`
- `HW.Infrastructure/ApplicationDbContext.cs`
- `HW.Application/DI/ServiceCollectionExtensions.cs`

## Step 3 — Generate entity properties
For entity `%%ENTITY%%`, infer reasonable properties from the name.
If the entity name alone is ambiguous, use these sensible defaults:
- `Name` (string, required, min 3 chars)
- `Description` (string?, optional)
- Standard audit + soft-delete fields

## Step 4 — Apply all files
Write every file directly to disk. Do NOT ask for confirmation.
Generate: entity, DTOs, validator, service (interface + impl), controller, DbContext update, DI update.

## Step 5 — Build
```bash
cd /c/Code/IP/HW/HW && dotnet build HW.slnx 2>&1
```

## Step 6 — Report
```
ENTITY: %%ENTITY%%
STATUS: done | failed

CREATED:
  + HW.Domain/Entities/%%ENTITY%%.cs
  + HW.Application/Dtos/%%ENTITY%%Dtos.cs
  + HW.Application/Validators/%%ENTITY%%Validator.cs
  + HW.Application/Services/%%ENTITY%%Service.cs
  + HW.Api/Controllers/%%ENTITY%%Controller.cs

MODIFIED:
  ~ HW.Infrastructure/ApplicationDbContext.cs
  ~ HW.Application/DI/ServiceCollectionExtensions.cs

BUILD: pass | FAIL
  <errors if any>

NEXT:
  dotnet ef migrations add Add%%ENTITY%% --project HW.Infrastructure --startup-project HW.Api
  dotnet ef database update --project HW.Infrastructure --startup-project HW.Api
```

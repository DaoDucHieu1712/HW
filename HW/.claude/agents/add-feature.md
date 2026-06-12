# Agent: add-feature

Generate a complete CQRS vertical slice for one entity — Domain → Application → Infrastructure → API — following this project's exact conventions.

**Trigger:** "add feature X" / "add {entity} feature" / "generate {entity} CRUD"

---

## Inputs required
- **Entity name** (PascalCase singular): e.g. `Product`
- **Properties**: name, type, required/optional (do not include `Id`, audit fields, or `IsDelete`)
- **Endpoints**: which of GET-list, GET-by-id, POST, PUT, DELETE to include
- **Value objects**: which properties (if any) should be value objects with domain validation
- **Auth**: which endpoints need `[Authorize]` (default: POST, PUT, DELETE)

If any input is missing, ask before generating.

---

## Step 1 — Load skills

Read these files before generating any code:
- `.claude/skills/domain-layer/skill.md`
- `.claude/skills/application-cqrs/skill.md`
- `.claude/skills/infrastructure-layer/skill.md`
- `.claude/skills/webapi-layer/skill.md`
- `.claude/skills/validation-fluent/skill.md`

Also read `CLAUDE.md` for hard rules.

---

## Step 2 — Read reference files

Read these actual source files to match exact style:
- `HW.Domain/Entities/Blog.cs`
- `HW.Application/Features/Blogs/Commands/CreateBlog/CreateBlogCommand.cs`
- `HW.Application/Features/Blogs/Commands/UpdateBlog/UpdateBlogCommand.cs`
- `HW.Application/Features/Blogs/Commands/DeleteBlog/DeleteBlogCommand.cs`
- `HW.Application/Features/Blogs/Queries/GetBlogs/GetBlogsQuery.cs`
- `HW.Application/Features/Blogs/Queries/GetBlogById/GetBlogByIdQuery.cs`
- `HW.Application/Features/Blogs/Dtos/BlogDtos.cs`
- `HW.Api/Controllers/BlogController.cs`
- `HW.Infrastructure/ApplicationDbContext.cs`

---

## Step 3 — Generate files in this order

### 3.1 Domain events — `HW.Domain/Events/{Feature}s/`
Create one file with all three event records:
```
{Feature}CreatedDomainEvent(string {Feature}Id, ...key fields)
{Feature}UpdatedDomainEvent(string {Feature}Id, ...key fields)
{Feature}DeletedDomainEvent(string {Feature}Id)
```
Each implements `IDomainEvent`.

### 3.2 Entity — `HW.Domain/Entities/{Feature}.cs`
- Extends `AggregateRoot`, implements `IAuditableEntity`, `ISoftDeleteEntity`
- `protected` parameterless constructor (EF proxy)
- Domain constructor sets properties and raises `{Feature}CreatedDomainEvent`
- Properties have `private set` — mutated only via domain methods
- `Update()` — null-checks each param, raises `{Feature}UpdatedDomainEvent`
- `SoftDelete()` — sets `IsDelete = true`, raises `{Feature}DeletedDomainEvent`
- `virtual` navigation properties
- If value objects: use value object types, not raw strings

### 3.3 (Optional) Value objects — `HW.Domain/ValueObjects/{Feature}{Prop}.cs`
Only generate if a property needs domain validation (length, format, business rule).
Each follows the `ValueObject<T>` pattern: private ctor, `Create()` factory, `FromPersistence()`, implicit `string` operator.

### 3.4 NotFoundException — `HW.Domain/Exceptions/{Feature}NotFoundException.cs`
```csharp
public class {Feature}NotFoundException : NotFoundException
{
    public {Feature}NotFoundException(string id)
        : base($"{Feature} with id '{id}' was not found.") { }
}
```

### 3.5 DTOs — `HW.Application/Features/{Feature}s/Dtos/{Feature}Dtos.cs`
One static class with all records:
- `{Feature}ResponseDto` — Id, all fields, audit fields (CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
- `{Feature}PagingRequestDto` — Search, PageIndex, PageSize
- `Create{Feature}RequestDto` — required/optional input fields only
- `Update{Feature}RequestDto` — Id (required), other fields nullable

### 3.6 Commands (one file each) — `HW.Application/Features/{Feature}s/Commands/`

**Create{Feature}Command.cs** — command record + validator + handler in one file:
- Command: `record Create{Feature}Command(...) : ICommand`
- Validator: validates required fields
- Handler: creates entity, calls `_repository.Add(entity)` — NO `_uow.ExecuteAsync()` (TransactionBehavior handles it)

**Update{Feature}Command.cs** — same structure:
- Command: `record Update{Feature}Command(string Id, ...) : ICommand`
- Validator: `Id` required; other fields optional with `.When(x => x.Field is not null)`
- Handler: `FindByIdAsync` → throw `{Feature}NotFoundException` if null → call `entity.Update(...)` → `_repository.Update(entity)`

**Delete{Feature}Command.cs** — same structure:
- Command: `record Delete{Feature}Command(string Id) : ICommand`
- Validator: `Id` required
- Handler: `FindByIdAsync` → throw `{Feature}NotFoundException` if null → `entity.SoftDelete()` → `_repository.Update(entity)`

### 3.7 Queries (one file each) — `HW.Application/Features/{Feature}s/Queries/`

**Get{Feature}sQuery.cs** — query + handler in one file:
- Query: `record Get{Feature}sQuery(string? Search, int PageIndex, int PageSize) : IQuery<PagedResult<{Feature}ResponseDto>>`
- Handler: `FindAll()` → filter by Search → `PagedResult<{Feature}>.CreateAsync()` → `.Adapt<PagedResult<{Feature}ResponseDto>>()`

**Get{Feature}ByIdQuery.cs** — query + handler in one file:
- Query: `record Get{Feature}ByIdQuery(string Id) : IQuery<{Feature}ResponseDto>`
- Handler: `FindByIdAsync(request.Id, ct)` → throw if null → `.Adapt<{Feature}ResponseDto>()`

### 3.8 Domain event handler — `HW.Application/Features/{Feature}s/Events/{Feature}CreatedDomainEventHandler.cs`
Handles `DomainEventWrapper<{Feature}CreatedDomainEvent>` — logs the event.

### 3.9 Controller — `HW.Api/Controllers/{Feature}Controller.cs`
- Route: `[Route("api/{feature}")]` (lowercase)
- Inject `ISender _sender` (not a service interface)
- Map each HTTP method to the corresponding command/query
- Apply `[Authorize]` to POST, PUT, DELETE
- GET → `Ok(ApiResponseFactory.Success(result))`
- Mutations → `NoContent()`

### 3.10 DbContext — MODIFY `HW.Infrastructure/ApplicationDbContext.cs`
Add DbSet:
```csharp
public virtual DbSet<{Feature}> {Feature}s { get; set; }
```
Add entity config inside `OnModelCreating`:
```csharp
builder.Entity<{Feature}>(b =>
{
    b.HasQueryFilter(x => x.IsDelete != true);
    // if value objects: add HasConversion(...) + HasMaxLength(...)
});
```

---

## Step 4 — Self-check before outputting

- [ ] All `using` directives present in every file
- [ ] Entity extends `AggregateRoot`, not `Entity` directly
- [ ] `protected` constructor (not `public`) on entity
- [ ] Domain events raised in constructor, `Update()`, and `SoftDelete()`
- [ ] Command handlers do NOT call `_uow.ExecuteAsync()` or `SaveChangesAsync()`
- [ ] `Delete` handler uses `entity.SoftDelete()` + `_repository.Update()` (not `_repository.Remove()`)
- [ ] `Update` handler only overwrites non-null fields via `entity.Update()`
- [ ] Validators co-located in same file as command/query
- [ ] DTOs are records in `Features/{Feature}s/Dtos/`
- [ ] Controller injects `ISender`, not a service interface
- [ ] Controller route is lowercase
- [ ] DbSet is `virtual`
- [ ] Query filter added in `OnModelCreating`

---

## Step 5 — Output format

Print each file in full with a header:

```
─── HW.Domain/Events/{Feature}s/{Feature}DomainEvents.cs ───
<complete file>

─── HW.Domain/Entities/{Feature}.cs ───────────────────────
<complete file>

─── HW.Domain/Exceptions/{Feature}NotFoundException.cs ────
<complete file>

─── HW.Application/Features/{Feature}s/Dtos/{Feature}Dtos.cs
<complete file>

─── HW.Application/Features/{Feature}s/Commands/Create{Feature}/Create{Feature}Command.cs
<complete file>

─── HW.Application/Features/{Feature}s/Commands/Update{Feature}/Update{Feature}Command.cs
<complete file>

─── HW.Application/Features/{Feature}s/Commands/Delete{Feature}/Delete{Feature}Command.cs
<complete file>

─── HW.Application/Features/{Feature}s/Queries/Get{Feature}s/Get{Feature}sQuery.cs
<complete file>

─── HW.Application/Features/{Feature}s/Queries/Get{Feature}ById/Get{Feature}ByIdQuery.cs
<complete file>

─── HW.Application/Features/{Feature}s/Events/{Feature}CreatedDomainEventHandler.cs
<complete file>

─── HW.Api/Controllers/{Feature}Controller.cs ─────────────
<complete file>

─── MODIFY: HW.Infrastructure/ApplicationDbContext.cs ──────
Add after last DbSet:
    public virtual DbSet<{Feature}> {Feature}s { get; set; }

Add inside OnModelCreating:
    builder.Entity<{Feature}>(b =>
    {
        b.HasQueryFilter(x => x.IsDelete != true);
    });

─── POST-APPLY STEPS ───────────────────────────────────────
dotnet ef migrations add Add{Feature} --project HW.Infrastructure --startup-project HW.Api
dotnet ef database update --project HW.Infrastructure --startup-project HW.Api
```

---

## Step 6 — Ask before applying

After printing all files, ask:
> "Apply these files to disk? (yes / no)"

If yes: write each new file using the Write tool, apply modifications with the Edit tool, then run:
```bash
dotnet build HW.slnx 2>&1
```
Report build result. If errors, list them and ask how to proceed — do NOT silently auto-fix.

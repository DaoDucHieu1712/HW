# HW Project — Stack Rules

## Solution layout
```
HW.Domain/          → entities (AggregateRoot), domain events, value objects, abstractions
HW.Application/     → CQRS commands/queries, behaviors, DTOs (records), Mapster mapping
HW.Infrastructure/  → EFRepository<T>, EFUnitOfWork, DbContext, interceptors, Outbox processor, DI
HW.Api/             → controllers (ISender), middleware, DI wiring, Program.cs
```

## Hard rules
- **No DbContext outside Infrastructure.** Handlers touch only `IEFRepository<T>` + `IUnitOfWork`.
- **No raw `new` for entities in controllers.** Entities are created inside command handlers.
- **No service layer.** Use CQRS commands and queries — no `I{Feature}Service` classes.
- **Async all the way.** Every handler method is `async Task` / `async Task<T>`.
- **Mapster for all mapping.** Never map by hand. Use `.Adapt<T>()` inline.
- **Soft delete only.** Never call `_repository.Remove()` on auditable entities — call `entity.SoftDelete()` then `_repository.Update(entity)`.
- **Domain methods.** Entity state changes go through named methods (`Update()`, `SoftDelete()`) that raise domain events — never assign properties directly from handlers.
- **Partial update pattern.** Only overwrite non-null fields inside entity's `Update()` method.
- **`string` IDs everywhere.** `Guid.NewGuid().ToString()` in Entity base constructor.
- **`virtual` navigation properties.** Required for EF Core lazy-loading proxies.
- **No `_uow` in command handlers.** `TransactionBehavior` wraps all `IBaseCommand` automatically via `ExecuteAsync()`.
- **DTOs are records.** Located in `HW.Application/Features/{Feature}s/Dtos/`.
- **Validators co-located.** Each validator lives in the same file as its command/query.

## Key types (exact signatures)
```csharp
// Entity base — HW.Domain.Abstractions.Entities
class Entity { string Id; }  // Id = Guid.NewGuid().ToString()

// AggregateRoot — HW.Domain.Abstractions.Entities
abstract class AggregateRoot : Entity
{
    IReadOnlyList<IDomainEvent> DomainEvents;
    protected void RaiseDomainEvent(IDomainEvent);
    void ClearDomainEvents();
}

// Audit — HW.Domain.Abstractions.Entities
interface IAuditableEntity { DateTimeOffset? CreatedAt/UpdatedAt; string? CreatedBy/UpdatedBy; }

// Soft delete — HW.Domain.Abstractions.Entities
interface ISoftDeleteEntity { bool? IsDelete; }

// Repository — HW.Domain.Abstractions.Repositories
interface IEFRepository<TEntity> where TEntity : Entity
{
    IQueryable<TEntity> FindAll(predicate?, includeProperties[]);      // AsNoTracking
    Task<TEntity> FindByIdAsync(string id, ct, includeProperties[]);   // AsTracking
    Task<TEntity> FindSingleAsync(predicate?, ct, includeProperties[]); // AsTracking
    void Add(TEntity); void AddRange(List<TEntity>);
    void Update(TEntity); void Remove(TEntity); void RemoveMultiple(List<TEntity>);
}

// Unit of Work — HW.Domain.Abstractions
interface IUnitOfWork : IAsyncDisposable
{
    Task SaveChangesAsync(CancellationToken ct = default);
    Task ExecuteAsync(Func<Task> action);  // opens transaction + converts domain events to outbox
}

// Paging — HW.Domain.Abstractions.Entities
class PagedResult<T> { Items, PageIndex, PageSize, TotalCount, TotalPages, HasNextPage, HasPreviousPage }
static Task<PagedResult<T>> CreateAsync(IQueryable<T> query, int pageIndex, int pageSize)

// CQRS interfaces — HW.Application.CQRS
interface ICommand : IRequest<Unit>, IBaseCommand { }
interface ICommand<TResponse> : IRequest<TResponse>, IBaseCommand { }
interface IQuery<TResponse> : IRequest<TResponse> { }
```

## Packages (actual versions in use)
| Package | Version | Layer |
|---------|---------|-------|
| MediatR | 12.4.1 | Application |
| Pomelo.EntityFrameworkCore.MySql | 8.0.3 | Infrastructure |
| Microsoft.EntityFrameworkCore.Proxies | 8.0.13 | Infrastructure |
| Newtonsoft.Json | latest | Infrastructure (Outbox serialization) |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 8.0.4 | Domain |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.23 | Domain |
| FluentValidation | 11.9.0 | Application |
| FluentValidation.DependencyInjectionExtensions | 11.9.0 | Application |
| Mapster | 7.4.0 | Application |
| Swashbuckle.AspNetCore | 6.6.2 | Api |

## DI registration pattern
- Validators: `services.AddValidatorsFromAssembly(HW.Application.AssemblyReference.Assembly)`
- MediatR handlers: `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(AssemblyReference.Assembly))`
- Pipeline behaviors: registered explicitly in order in `AddApplicationServices()`
- Repositories: `services.AddScoped(typeof(IEFRepository<>), typeof(EFRepository<>))`
- No per-feature DI registration needed — MediatR and FluentValidation scan automatically
- DbContext: `HW.Infrastructure/DI/ServiceCollectionExtensions.AddMariaDbConfiguration(config, isDev)`

## Middleware pipeline order (Program.cs)
```
UseCors → UseAuthentication → UseAuthorization
→ UseMiddleware<ExceptionHandlingMiddleware>
→ MapControllers
```
Note: No `FluentValidationMiddleware` — validation runs inside the MediatR pipeline via `ValidationBehavior`.

## Naming conventions
- Entities: `PascalCase` singular — `Blog`, `Product`
- DbSets: `PascalCase` plural — `Blogs`, `Products`
- Feature folder: `HW.Application/Features/{Feature}s/` (plural)
- DTOs: records — `{Feature}ResponseDto`, `Create{Feature}RequestDto`, `Update{Feature}RequestDto`
- Commands: `Create{Feature}Command`, `Update{Feature}Command`, `Delete{Feature}Command`
- Queries: `Get{Feature}sQuery`, `Get{Feature}ByIdQuery`
- Controllers: `{Feature}Controller`, route `api/{feature}` (lowercase singular)
- Domain events: `{Feature}CreatedDomainEvent`, `{Feature}UpdatedDomainEvent`, `{Feature}DeletedDomainEvent`
- Validators: `{CommandName}Validator` — co-located in same file as command/query

## Agents
- `.claude/agents/scaffold-solution.md` — bootstrap a brand-new solution
- `.claude/agents/add-feature.md` — generate full CQRS vertical slice for one entity

## Skills (building blocks used by agents)
- `.claude/skills/dotnet-project-scaffold/` — .csproj templates, CLI commands
- `.claude/skills/domain-layer/` — Entity, AggregateRoot, IDomainEvent, ValueObject, IRepository, IUoW
- `.claude/skills/application-cqrs/` — CQRS interfaces, command/query handlers, behaviors, DTOs
- `.claude/skills/infrastructure-layer/` — EFRepository, EFUnitOfWork (with outbox), DbContext, interceptor
- `.claude/skills/webapi-layer/` — controllers (ISender), ApiResponse, ExceptionHandlingMiddleware, Swagger
- `.claude/skills/ef-dapper-setup/` — MariaDB config, migrations, retry strategy
- `.claude/skills/di-scrutor/` — DI registration patterns, assembly scanning
- `.claude/skills/validation-fluent/` — FluentValidation co-located validators, ValidationBehavior

## Patterns (read when implementing a specific behaviour)
- `.claude/patterns/01-exception-hierarchy.md` — DomainException → NotFoundException / BadRequestException / TokenException, middleware mapping
- `.claude/patterns/02-repository-uow.md` — FindAll (no-track) vs FindByIdAsync (tracked), ExecuteAsync for mutations
- `.claude/patterns/03-soft-delete.md` — entity.SoftDelete() method, query filter, IsDelete = true
- `.claude/patterns/04-dto-records.md` — record DTOs in static class, Mapster with records
- `.claude/patterns/05-api-response.md` — ApiResponseFactory methods, controller shapes, middleware vs controller error handling
- `.claude/patterns/06-partial-update.md` — nullable fields in command, null-check per field in entity.Update()
- `.claude/patterns/07-pagination.md` — PagedResult.CreateAsync, filter→sort→page order, Mapster PagedResult mapping
- `.claude/patterns/08-jwt-auth.md` — token claims, login/register flow, VerifyTokenAsync, [Authorize] usage

## Guidelines (read before writing any code)
- `.claude/guidelines/coding-style.md` — naming, async, null handling, expression bodies, string conventions
- `.claude/guidelines/error-handling.md` — what to throw where, exception ownership per layer, logging
- `.claude/guidelines/api-design.md` — routes, HTTP methods, status codes, controller structure, Swagger
- `.claude/guidelines/layer-boundaries.md` — what belongs in each project, allowed cross-layer calls, mapping boundary

# Guideline: Layer Boundaries

## Dependency rule

Dependencies flow **inward only**:

```
HW.Api  →  HW.Application  →  HW.Domain
HW.Infrastructure  →  HW.Application  →  HW.Domain
HW.Api  →  HW.Infrastructure   (only for DI wiring in Program.cs)
```

No layer may reference a layer further out. Domain knows nothing about EF, HTTP, or FluentValidation.

## What belongs where

### HW.Domain
✅ Entity classes (extending `Entity`)
✅ Domain interfaces (`IEFRepository<T>`, `IUnitOfWork`, `IAuditableEntity`, `ISoftDeleteEntity`)
✅ Domain exception types (`DomainException`, `NotFoundException`, `BadRequestException`)
✅ Value objects and enums
✅ `PagedResult<T>`
❌ EF Core attributes or `DbSet`
❌ HTTP types (`HttpContext`, `IActionResult`)
❌ FluentValidation
❌ DTOs

### HW.Application
✅ Service interfaces + implementations (`IBlogService`, `BlogService`)
✅ DTOs (as `record` types in `static class {Feature}Dtos`)
✅ FluentValidation validators
✅ Mapster mapping configs (`IMappingRegister`)
✅ DI extension (`AddApplicationServices`)
❌ `DbContext` or EF Core types
❌ `HttpContext` or ASP.NET Core middleware
❌ Infrastructure implementations (`EFRepository`, `EFUnitOfWork`)
❌ JWT token generation

### HW.Infrastructure
✅ `ApplicationDbContext`
✅ `EFRepository<T>` and `EFUnitOfWork`
✅ `AuditableEntitiesInterceptor`
✅ MariaDB/Pomelo configuration
✅ EF Core migrations
✅ DI extension (`AddMariaDbConfiguration`, `AddInfrastructureServices`)
✅ `UserInfo` (multi-tenant context)
❌ Business logic
❌ DTOs or validators
❌ HTTP types

### HW.Api
✅ Controllers
✅ Middleware (`ExceptionHandlingMiddleware`, `FluentValidationMiddleware`)
✅ `ApiResponse<T>` and `ApiResponseFactory`
✅ Swagger configuration
✅ `Program.cs` — DI wiring and pipeline setup
✅ `appsettings.json`
❌ Business logic — controllers call services only
❌ Direct repository access
❌ EF Core or DbContext

## Allowed cross-layer calls

```
Controller → IService method             ✅
Service    → IEFRepository<T> method     ✅
Service    → IUnitOfWork.ExecuteAsync()  ✅
Service    → other IService (via DI)     ✅ (inject sparingly)
Service    → UserManager<AppUser>        ✅ (AuthService only)

Controller → IEFRepository<T>           ❌ bypasses service layer
Controller → DbContext                   ❌
Service    → DbContext                   ❌ breaks abstraction
Service    → ApplicationDbContext        ❌
```

## DI lifetimes — cross-layer rules

A scoped service cannot depend on a singleton-scoped service that holds state. Current registrations:

```
Singleton:  UserInfo, AuditableEntitiesInterceptor
Scoped:     ApplicationDbContext (via AddDbContext), EFRepository<T>, EFUnitOfWork, IXService
Transient:  (none currently)
```

**Rule:** Never inject a scoped dependency into a singleton. Use `IServiceProvider` + `CreateScope()` if needed (e.g. background jobs).

## Validation boundary

FluentValidation validators live in Application but run in the Api middleware pipeline:

```
HTTP Request
  → FluentValidationMiddleware (Api layer, resolves IValidator<T> from DI)
      → AbstractValidator<T> (Application layer)
          → [async DB check via IEFRepository — Infrastructure via DI]
  → Controller action (only reaches here if validation passes)
```

## Mapping boundary

Mapster mapping (Application layer) is the only place `Entity → DTO` conversion happens:

```csharp
// ✅ Map in service using .Adapt<>()
return entity.Adapt<BlogResponseDto>();

// ❌ Map in controller
var dto = new BlogResponseDto(entity.Id, entity.Title, ...);

// ❌ Return entities from service
public async Task<Blog> FindById(string id)  // exposes domain model to API
```

# Guideline: Error Handling

## Principle
Errors flow up — services throw typed exceptions, middleware catches and maps them. Controllers and repositories never catch exceptions.

## Error ownership by layer

| Layer | Responsibility |
|-------|---------------|
| Domain | Define exception types (`DomainException` hierarchy) |
| Application (Services) | Throw typed exceptions for business violations |
| Infrastructure | Let EF/DB exceptions bubble (they become 500) |
| API (Controllers) | Never catch — no try/catch |
| API (Middleware) | Catch all, map to `ApiResponse`, return correct HTTP code |

## Exception → HTTP mapping

```
DomainException (any subclass)    → 400 Bad Request
  ├── BadRequestException         → 400
  ├── NotFoundException           → 400 (consider overriding to 404)
  └── IdentityException.TokenException → 400

FluentValidation.ValidationException → 422 Unprocessable Entity
ArgumentException                → 400 Bad Request
UnauthorizedAccessException      → 401 Unauthorized
Everything else                  → 500 Internal Server Error
```

## What to throw where

```csharp
// Service: not found
var entity = await _repo.FindByIdAsync(id);
if (entity is null || entity.IsDelete == true)
    throw new NotFoundException($"Blog '{id}' not found.");  // or concrete subclass

// Service: duplicate / uniqueness violation
var exists = await _repo.FindAll(x => x.Title == dto.Title && x.IsDelete != true).AnyAsync();
if (exists)
    throw new BadRequestException("A blog with this title already exists.");

// Service: business rule violation
if (dto.Price <= 0)
    throw new BadRequestException("Price must be greater than zero.");

// Auth service: invalid credentials (don't reveal which field is wrong)
throw new UnauthorizedAccessException("Invalid username or password.");

// Auth service: token problems
throw new IdentityException.TokenException("Token is expired or invalid.");

// Infrastructure: let EF exceptions bubble as 500
// → Do NOT catch DbUpdateException in the service layer
```

## What NOT to do

```csharp
// ❌ Return null instead of throwing
public async Task<BlogResponseDto?> FindById(string id)
{
    var entity = await _repo.FindByIdAsync(id);
    return entity?.Adapt<BlogResponseDto>();  // null leaks to controller
}

// ❌ Catch-and-rethrow without value
try { ... }
catch (Exception ex) { throw new Exception("Error", ex); }

// ❌ Use generic Exception from service
throw new Exception("Blog not found");   // becomes 500, not 404

// ❌ Catch in controller
[HttpGet("{id}")]
public async Task<IActionResult> GetById(string id)
{
    try { return Ok(await _service.FindById(id)); }
    catch (Exception ex) { return BadRequest(ex.Message); }  // hides stack trace, bypasses middleware
}

// ✅ Throw typed, let middleware handle
public async Task<BlogResponseDto> FindById(string id)
{
    var entity = await _repo.FindByIdAsync(id);
    if (entity is null) throw new NotFoundException($"Blog '{id}' not found.");
    return entity.Adapt<BlogResponseDto>();
}
```

## Logging

Only `ExceptionHandlingMiddleware` logs unhandled exceptions:
```csharp
_logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
```

For business-level logging inside services, inject `ILogger<T>`:
```csharp
public class BlogService : IBlogService
{
    private readonly ILogger<BlogService> _logger;

    public async Task Remove(string id)
    {
        _logger.LogInformation("Soft-deleting blog {Id}", id);
        await _uow.ExecuteAsync(async () => { ... });
    }
}
```

## Validation errors (FluentValidation)

Validation runs in `FluentValidationMiddleware` before the controller action:
- POST/PUT/PATCH requests only
- Validator is resolved from DI by DTO type
- Returns 422 with `errors[]` array — controller action never runs on failure

For service-level validation (e.g. uniqueness checks requiring DB):
```csharp
// Option A: throw in service
var exists = await _repo.FindAll(x => x.Title == dto.Title).AnyAsync();
if (exists) throw new BadRequestException("Title already exists.");

// Option B: async FluentValidation rule (see validation-fluent skill)
RuleFor(x => x.Title)
    .MustAsync(async (title, ct) => !await repo.FindAll(x => x.Title == title).AnyAsync(ct))
    .WithMessage("Title already exists.");
```

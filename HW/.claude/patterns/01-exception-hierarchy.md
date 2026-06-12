# Pattern: Exception Hierarchy

## What
Typed domain exceptions that flow through `ExceptionHandlingMiddleware` and map to specific HTTP status codes automatically.

## Hierarchy (actual code)

```
Exception
└── DomainException (abstract)          → always 400 Bad Request
    ├── NotFoundException (abstract)    → 400 (override to 404 if needed)
    ├── BadRequestException             → 400
    └── IdentityException
        └── TokenException             → 400
```

## Source

```csharp
// DomainException — base for all domain errors
public abstract class DomainException : Exception
{
    protected DomainException(string title, string message)
        : base(message) => Title = title;

    public string Title { get; }
}

// For "not found" scenarios
public abstract class NotFoundException : DomainException
{
    protected NotFoundException(string message)
        : base("Not Found", message) { }
}

// For bad input / business rule violations
public class BadRequestException : DomainException
{
    public BadRequestException(string message)
        : base("Bad Request", message) { }
}

// For auth/token failures
public static class IdentityException
{
    public class TokenException : DomainException
    {
        public TokenException(string message)
            : base("Token Exception", message) { }
    }
}
```

## Middleware mapping

```csharp
// ExceptionHandlingMiddleware maps to HTTP codes:
DomainException          → 400  (message + Title as error)
ValidationException      → 422  (field errors from FluentValidation)
ArgumentException        → 400  (message)
UnauthorizedAccessException → 401
else                     → 500
```

## Usage rules

| Scenario | Exception to throw |
|---|---|
| Entity not found by ID | Concrete subclass of `NotFoundException` |
| Duplicate name/email/code | `BadRequestException` |
| Business rule violated | `BadRequestException` |
| Invalid JWT token | `IdentityException.TokenException` |
| User-facing input error | `BadRequestException` |
| Unexpected / programming error | Let it bubble as 500 |

## How to create a feature-specific NotFoundException

```csharp
// HW.Domain/Exceptions/ProductNotFoundException.cs
namespace HW.Domain.Exceptions;

public class ProductNotFoundException : NotFoundException
{
    public ProductNotFoundException(string id)
        : base($"Product with ID '{id}' was not found.") { }
}
```

Usage in service:
```csharp
var entity = await _repo.FindByIdAsync(id);
if (entity is null)
    throw new ProductNotFoundException(id);
```

## What NOT to do

```csharp
// ❌ Don't throw plain Exception from services
throw new Exception("Not found");

// ❌ Don't use ArgumentException for domain logic
throw new ArgumentException("Product already exists");

// ❌ Don't catch-and-return null to hide errors
var entity = await _repo.FindByIdAsync(id);
if (entity is null) return null; // controller gets null, no error surfaced

// ✅ Throw typed exceptions — middleware handles the rest
throw new ProductNotFoundException(id);
throw new BadRequestException("A product with this name already exists.");
```

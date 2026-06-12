# Skill: validation-fluent

FluentValidation validators — co-located with commands/queries, registered via assembly scanning.

---

## Location and co-location rule

Validators live **in the same file** as their command or query — NOT in a separate `Validators/` folder.

```
HW.Application/Features/{Feature}/Commands/Create{Feature}/Create{Feature}Command.cs
  ↳ contains: Create{Feature}Command record
  ↳ contains: Create{Feature}CommandValidator : AbstractValidator<Create{Feature}Command>
  ↳ contains: Create{Feature}CommandHandler
```

---

## Command validator pattern
```csharp
public record Create{Feature}Command(string? Prop1, string? Prop2) : ICommand;

public class Create{Feature}CommandValidator : AbstractValidator<Create{Feature}Command>
{
    public Create{Feature}CommandValidator()
    {
        RuleFor(x => x.Prop1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Prop2).MaximumLength(500).When(x => x.Prop2 is not null);
    }
}
```

For update commands — all fields optional except `Id`:
```csharp
public class Update{Feature}CommandValidator : AbstractValidator<Update{Feature}Command>
{
    public Update{Feature}CommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Prop1).MaximumLength(200).When(x => x.Prop1 is not null);
    }
}
```

For delete commands — only ID validation:
```csharp
public class Delete{Feature}CommandValidator : AbstractValidator<Delete{Feature}Command>
{
    public Delete{Feature}CommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
```

---

## Registration — `Program.cs`
```csharp
// Auto-discovers ALL AbstractValidator<T> in the Application assembly
builder.Services.AddValidatorsFromAssembly(HW.Application.AssemblyReference.Assembly);
```

No per-validator registration needed — assembly scanning picks up everything automatically.

---

## How validation is triggered

1. Controller calls `_sender.Send(new Create{Feature}Command(...))`.
2. MediatR pipeline runs: **Logging → ValidationBehavior → TransactionBehavior → Handler**.
3. `ValidationBehavior` resolves all `IValidator<TRequest>` from DI and calls `Validate()`.
4. On failure: throws `ValidationException` → caught by `ExceptionHandlingMiddleware` → HTTP 422.

There is no separate `FluentValidationMiddleware` in this project.

---

## Common validation rules by field type

| Field | Rules |
|-------|-------|
| Required string | `.NotEmpty()` |
| String min length | `.MinimumLength(N)` |
| String max length | `.MaximumLength(N)` |
| Email | `.NotEmpty().EmailAddress()` |
| Positive number | `.GreaterThan(0)` |
| Non-negative | `.GreaterThanOrEqualTo(0)` |
| Range | `.InclusiveBetween(min, max)` |
| Optional field with rule | `.When(x => x.Field is not null)` |
| Enum | `.IsInEnum()` |
| Password | `.MinimumLength(3)` (project policy) |

---

## Async DB validator (check uniqueness)
```csharp
public class Create{Feature}CommandValidator : AbstractValidator<Create{Feature}Command>
{
    public Create{Feature}CommandValidator(IEFRepository<{Feature}> repo)
    {
        RuleFor(x => x.Prop1)
            .NotEmpty()
            .MustAsync(async (name, ct) =>
                !await repo.FindAll(x => x.Prop1 == name).AnyAsync(ct))
            .WithMessage("A {feature} with this name already exists.");
    }
}
```

> When injecting a repository, the validator constructor gets it from DI automatically via assembly scanning.

---

## ValidationBehavior — `HW.Application/Behaviors/ValidationBehavior.cs`
```csharp
using FluentValidation;
using MediatR;

namespace HW.Application.Behaviors;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

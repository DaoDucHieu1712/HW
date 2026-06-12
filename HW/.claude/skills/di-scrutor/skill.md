# Skill: di-scrutor

DI registration patterns. The project uses MediatR for application logic — no manual service registrations per feature.

---

## Application DI — `HW.Application/DI/ServiceCollectionExtensions.cs`
```csharp
using HW.Application.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace HW.Application.DI;

public static class ServiceCollectionExtensions
{
    public static void AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(AssemblyReference.Assembly));

        // Pipeline order: Logging → Validation → Transaction → Handler
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
    }
}
```

**Adding a new feature requires NO changes here.** MediatR scans the assembly and registers all handlers automatically.

---

## Infrastructure DI — `HW.Infrastructure/DI/ServiceCollectionExtensions.cs`
```csharp
public static void AddInfrastructureServices(this IServiceCollection services)
{
    services.AddHttpContextAccessor();
    services.AddScoped(typeof(IEFRepository<>), typeof(EFRepository<>)); // generic open type
    services.AddScoped<IUnitOfWork, EFUnitOfWork>();
    services.AddHostedService<Outbox.OutboxMessageProcessor>();
}

public static void AddInterceptorDbContext(this IServiceCollection services)
{
    services.AddSingleton<AuditableEntitiesInterceptor>();
}
```

---

## Validator registration — `Program.cs`
```csharp
// Auto-discovers all AbstractValidator<T> in the Application assembly
builder.Services.AddValidatorsFromAssembly(HW.Application.AssemblyReference.Assembly);
```

Validators are co-located with their commands — no extra registration per validator.

---

## Lifetime rules

| Type | Lifetime | Reason |
|------|----------|--------|
| `IEFRepository<T>` | Scoped | One DbContext per request |
| `IUnitOfWork` | Scoped | Shares same DbContext as repositories |
| MediatR handlers | Transient (auto) | MediatR default |
| `IPipelineBehavior<,>` | Transient | Registered explicitly |
| `IValidator<T>` | Scoped | FluentValidation DI default |
| `AuditableEntitiesInterceptor` | Singleton | Stateless |
| `OutboxMessageProcessor` | Hosted (Singleton) | BackgroundService |

---

## Scrutor assembly scanning (if you need to add explicit services)

If a future feature needs a non-MediatR service (e.g., a Dapper query service), add Scrutor:
```bash
dotnet add HW.Application package Scrutor
```

Then in `AddApplicationServices()`:
```csharp
services.Scan(scan => scan
    .FromAssemblyOf<AssemblyReference>()
    .AddClasses(classes => classes
        .Where(t => t.Name.EndsWith("QueryService") && !t.IsInterface))
    .AsImplementedInterfaces()
    .WithScopedLifetime());
```

---

## AssemblyReference marker class

Each project has one for assembly discovery:
```csharp
// HW.Application/AssemblyReference.cs
namespace HW.Application;
public sealed class AssemblyReference
{
    public static readonly System.Reflection.Assembly Assembly = typeof(AssemblyReference).Assembly;
}
```

---

## Decorator pattern with Scrutor (advanced)

To add cross-cutting concerns without changing a service:
```csharp
services.AddScoped<I{Feature}Service, {Feature}Service>();
services.Decorate<I{Feature}Service, Logging{Feature}Service>();
```

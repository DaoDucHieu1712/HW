# Skill: application-cqrs

CQRS with MediatR — the default pattern in this project. Commands, queries, behaviors, DTOs.

---

## CQRS interfaces — `HW.Application/CQRS/`

```csharp
// ICommand.cs
using MediatR;

namespace HW.Application.CQRS;

public interface IBaseCommand { }

public interface ICommand : IRequest<Unit>, IBaseCommand { }
public interface ICommand<TResponse> : IRequest<TResponse>, IBaseCommand { }
```

```csharp
// IQuery.cs
using MediatR;

namespace HW.Application.CQRS;

public interface IQuery<TResponse> : IRequest<TResponse> { }
```

```csharp
// ICommandHandler.cs
using MediatR;

namespace HW.Application.CQRS;

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Unit>
    where TCommand : ICommand { }

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse> { }
```

```csharp
// IQueryHandler.cs
using MediatR;

namespace HW.Application.CQRS;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse> { }
```

---

## Feature folder structure

```
HW.Application/Features/{Feature}/
├── Commands/
│   ├── Create{Feature}/
│   │   └── Create{Feature}Command.cs       ← command + validator + handler in one file
│   ├── Update{Feature}/
│   │   └── Update{Feature}Command.cs
│   └── Delete{Feature}/
│       └── Delete{Feature}Command.cs
├── Queries/
│   ├── Get{Feature}s/
│   │   └── Get{Feature}sQuery.cs           ← query + handler in one file
│   └── Get{Feature}ById/
│       └── Get{Feature}ByIdQuery.cs
├── Dtos/
│   └── {Feature}Dtos.cs                    ← all record DTOs in one static class
└── Events/
    └── {Feature}CreatedDomainEventHandler.cs
```

---

## DTOs — `HW.Application/Features/{Feature}/Dtos/{Feature}Dtos.cs`
```csharp
namespace HW.Application.Features.{Feature}s.Dtos;

public static class {Feature}Dtos
{
    public record {Feature}ResponseDto(
        string Id,
        string Prop1,
        string? Prop2,
        DateTimeOffset? CreatedAt,
        string? CreatedBy,
        DateTimeOffset? UpdatedAt,
        string? UpdatedBy);

    public record {Feature}PagingRequestDto(string? Search, int PageIndex, int PageSize);

    public record Create{Feature}RequestDto(string? Prop1, string? Prop2);

    public record Update{Feature}RequestDto(string Id, string? Prop1, string? Prop2);
}
```

---

## Create command — `Commands/Create{Feature}/Create{Feature}Command.cs`
```csharp
using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using MediatR;

namespace HW.Application.Features.{Feature}s.Commands.Create{Feature};

public record Create{Feature}Command(string? Prop1, string? Prop2) : ICommand;

public class Create{Feature}CommandValidator : AbstractValidator<Create{Feature}Command>
{
    public Create{Feature}CommandValidator()
    {
        RuleFor(x => x.Prop1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Prop2).MaximumLength(500).When(x => x.Prop2 is not null);
    }
}

public class Create{Feature}CommandHandler : ICommandHandler<Create{Feature}Command>
{
    private readonly IEFRepository<{Feature}> _repository;

    public Create{Feature}CommandHandler(IEFRepository<{Feature}> repository)
        => _repository = repository;

    public Task<Unit> Handle(Create{Feature}Command request, CancellationToken ct)
    {
        _repository.Add(new {Feature}(request.Prop1!, request.Prop2));
        return Unit.Task;
    }
}
```

**Note:** No `_uow.ExecuteAsync()` in command handlers. `TransactionBehavior` wraps all `IBaseCommand` automatically.

---

## Update command — `Commands/Update{Feature}/Update{Feature}Command.cs`
```csharp
using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.{Feature}s.Commands.Update{Feature};

public record Update{Feature}Command(string Id, string? Prop1, string? Prop2) : ICommand;

public class Update{Feature}CommandValidator : AbstractValidator<Update{Feature}Command>
{
    public Update{Feature}CommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Prop1).MaximumLength(200).When(x => x.Prop1 is not null);
    }
}

public class Update{Feature}CommandHandler : ICommandHandler<Update{Feature}Command>
{
    private readonly IEFRepository<{Feature}> _repository;

    public Update{Feature}CommandHandler(IEFRepository<{Feature}> repository)
        => _repository = repository;

    public async Task<Unit> Handle(Update{Feature}Command request, CancellationToken ct)
    {
        var entity = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new {Feature}NotFoundException(request.Id);

        entity.Update(request.Prop1, request.Prop2);
        _repository.Update(entity);
        return Unit.Value;
    }
}
```

---

## Delete command — `Commands/Delete{Feature}/Delete{Feature}Command.cs`
```csharp
using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.{Feature}s.Commands.Delete{Feature};

public record Delete{Feature}Command(string Id) : ICommand;

public class Delete{Feature}CommandValidator : AbstractValidator<Delete{Feature}Command>
{
    public Delete{Feature}CommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class Delete{Feature}CommandHandler : ICommandHandler<Delete{Feature}Command>
{
    private readonly IEFRepository<{Feature}> _repository;

    public Delete{Feature}CommandHandler(IEFRepository<{Feature}> repository)
        => _repository = repository;

    public async Task<Unit> Handle(Delete{Feature}Command request, CancellationToken ct)
    {
        var entity = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new {Feature}NotFoundException(request.Id);

        entity.SoftDelete();
        _repository.Update(entity);
        return Unit.Value;
    }
}
```

---

## Get list query — `Queries/Get{Feature}s/Get{Feature}sQuery.cs`
```csharp
using HW.Application.CQRS;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using Mapster;
using static HW.Application.Features.{Feature}s.Dtos.{Feature}Dtos;

namespace HW.Application.Features.{Feature}s.Queries.Get{Feature}s;

public record Get{Feature}sQuery(string? Search, int PageIndex, int PageSize)
    : IQuery<PagedResult<{Feature}ResponseDto>>;

public class Get{Feature}sQueryHandler : IQueryHandler<Get{Feature}sQuery, PagedResult<{Feature}ResponseDto>>
{
    private readonly IEFRepository<{Feature}> _repository;

    public Get{Feature}sQueryHandler(IEFRepository<{Feature}> repository)
        => _repository = repository;

    public async Task<PagedResult<{Feature}ResponseDto>> Handle(Get{Feature}sQuery request, CancellationToken ct)
    {
        var source = _repository.FindAll();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            source = source.Where(x => x.Prop1.ToLower().Contains(search));
        }

        var paged = await PagedResult<{Feature}>.CreateAsync(source, request.PageIndex, request.PageSize);
        return paged.Adapt<PagedResult<{Feature}ResponseDto>>();
    }
}
```

---

## Get by ID query — `Queries/Get{Feature}ById/Get{Feature}ByIdQuery.cs`
```csharp
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using Mapster;
using static HW.Application.Features.{Feature}s.Dtos.{Feature}Dtos;

namespace HW.Application.Features.{Feature}s.Queries.Get{Feature}ById;

public record Get{Feature}ByIdQuery(string Id) : IQuery<{Feature}ResponseDto>;

public class Get{Feature}ByIdQueryHandler : IQueryHandler<Get{Feature}ByIdQuery, {Feature}ResponseDto>
{
    private readonly IEFRepository<{Feature}> _repository;

    public Get{Feature}ByIdQueryHandler(IEFRepository<{Feature}> repository)
        => _repository = repository;

    public async Task<{Feature}ResponseDto> Handle(Get{Feature}ByIdQuery request, CancellationToken ct)
    {
        var entity = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new {Feature}NotFoundException(request.Id);

        return entity.Adapt<{Feature}ResponseDto>();
    }
}
```

---

## Domain event handler — `Events/{Feature}CreatedDomainEventHandler.cs`
```csharp
using HW.Application.Abstractions.Events;
using HW.Domain.Events.{Feature}s;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HW.Application.Features.{Feature}s.Events;

public class {Feature}CreatedDomainEventHandler
    : INotificationHandler<DomainEventWrapper<{Feature}CreatedDomainEvent>>
{
    private readonly ILogger<{Feature}CreatedDomainEventHandler> _logger;

    public {Feature}CreatedDomainEventHandler(ILogger<{Feature}CreatedDomainEventHandler> logger)
        => _logger = logger;

    public Task Handle(DomainEventWrapper<{Feature}CreatedDomainEvent> notification, CancellationToken ct)
    {
        _logger.LogInformation("[DomainEvent] {Feature} created — Id: {Id}", notification.Event.{Feature}Id);
        return Task.CompletedTask;
    }
}
```

---

## Pipeline behaviors (registered in `AddApplicationServices`)

### LoggingBehavior — `HW.Application/Behaviors/LoggingBehavior.cs`
Logs every request name before/after.

### ValidationBehavior — `HW.Application/Behaviors/ValidationBehavior.cs`
Runs all `IValidator<TRequest>` before the handler; throws `ValidationException` on failure.

### TransactionBehavior — `HW.Application/Behaviors/TransactionBehavior.cs`
Checks `request is IBaseCommand` — if true, wraps the handler call in `_uow.ExecuteAsync()`.
This means **command handlers must NOT call `_uow.ExecuteAsync()` themselves**.

Pipeline order: **Logging → Validation → Transaction → Handler**

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

---

## Rules
- **No service layer.** Commands and queries replace services entirely.
- **No `_uow` in handlers.** `TransactionBehavior` handles the transaction for all `IBaseCommand`.
- **Validators co-located** with their command/query in the same file.
- **DTOs are records** in `Features/{Feature}/Dtos/{Feature}Dtos.cs`.
- **Queries never mutate.** Only `ICommand`/`IBaseCommand` trigger transaction wrapping.
- **`FindAll()` for queries** (no-tracking), **`FindByIdAsync()` for mutations** (tracking).
- Mapster `.Adapt<T>()` used inline — no separate mapping config files needed.

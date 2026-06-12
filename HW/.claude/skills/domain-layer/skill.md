# Skill: domain-layer

Exact code for all domain abstractions and entity patterns. Copy these verbatim.

---

## Entity base — `HW.Domain/Abstractions/Entities/Entity.cs`
```csharp
namespace HW.Domain.Abstractions.Entities;

public class Entity
{
    protected Entity()
    {
        Id = Guid.NewGuid().ToString();
    }
    public string Id { get; set; }
}
```

## AggregateRoot — `HW.Domain/Abstractions/Entities/AggregateRoot.cs`
```csharp
using System.ComponentModel.DataAnnotations.Schema;
using HW.Domain.Abstractions.Events;

namespace HW.Domain.Abstractions.Entities;

public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    [NotMapped]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

## IDomainEvent — `HW.Domain/Abstractions/Events/IDomainEvent.cs`
```csharp
using MediatR;

namespace HW.Domain.Abstractions.Events;

public interface IDomainEvent : INotification { }
```

## IAuditableEntity — `HW.Domain/Abstractions/Entities/IAuditableEntity.cs`
```csharp
namespace HW.Domain.Abstractions.Entities;

public interface IAuditableEntity
{
    DateTimeOffset? CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
```

## ISoftDeleteEntity — `HW.Domain/Abstractions/Entities/ISoftDeleteEntity.cs`
```csharp
namespace HW.Domain.Abstractions.Entities;

public interface ISoftDeleteEntity
{
    bool? IsDelete { get; set; }
}
```

## IEFRepository — `HW.Domain/Abstractions/Repositories/IEFRepository.cs`
```csharp
using HW.Domain.Abstractions.Entities;
using System.Linq.Expressions;

namespace HW.Domain.Abstractions.Repositories;

public interface IEFRepository<TEntity>
    where TEntity : Entity
{
    IQueryable<TEntity> FindAll(
        Expression<Func<TEntity, bool>>? predicate = null,
        params Expression<Func<TEntity, object>>[] includeProperties);

    Task<TEntity> FindByIdAsync(
        string Id,
        CancellationToken cancellationToken = default,
        params Expression<Func<TEntity, object>>[] includeProperties);

    Task<TEntity> FindSingleAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default,
        params Expression<Func<TEntity, object>>[] includeProperties);

    void Add(TEntity entity);
    void AddRange(List<TEntity> entity);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    void RemoveMultiple(List<TEntity> entities);
}
```

## IUnitOfWork — `HW.Domain/Abstractions/IUnitOfWork.cs`
```csharp
namespace HW.Domain.Abstractions;

public interface IUnitOfWork : IAsyncDisposable
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<Task> action);
}
```

## PagedResult — `HW.Domain/Abstractions/Entities/PagedResult.cs`
```csharp
using Microsoft.EntityFrameworkCore;

namespace HW.Domain.Abstractions.Entities;

public class PagedResult<T>
{
    public const int UpperPageSize = 100;
    public const int DefaultPageSize = 10;
    public const int DefaultPageIndex = 1;

    public PagedResult(List<T> items, int pageIndex, int pageSize, int totalCount)
    {
        Items = items;
        PageIndex = pageIndex;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
    }
    public PagedResult() { }

    public List<T> Items { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; private set; }
    public bool HasNextPage => PageIndex * PageSize < TotalCount;
    public bool HasPreviousPage => PageIndex > 1;

    public static async Task<PagedResult<T>> CreateAsync(IQueryable<T> query, int pageIndex, int pageSize)
    {
        pageIndex = pageIndex <= 0 ? DefaultPageIndex : pageIndex;
        pageSize = pageSize <= 0 ? DefaultPageSize : pageSize > UpperPageSize ? UpperPageSize : pageSize;
        var totalCount = await query.CountAsync();
        var items = await query.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        return new(items, pageIndex, pageSize, totalCount);
    }

    public static PagedResult<T> Create(List<T> items, int pageIndex, int pageSize, int totalCount)
        => new(items, pageIndex, pageSize, totalCount);
}
```

## ValueObject base — `HW.Domain/Abstractions/ValueObjects/ValueObject.cs`
```csharp
namespace HW.Domain.Abstractions.ValueObjects;

public abstract class ValueObject<T> : IEquatable<T> where T : ValueObject<T>
{
    public abstract IEnumerable<object> GetAtomicValues();

    public bool Equals(T? other)
        => other is not null && GetAtomicValues().SequenceEqual(other.GetAtomicValues());

    public override bool Equals(object? obj)
        => obj is T other && Equals(other);

    public override int GetHashCode()
        => GetAtomicValues().Aggregate(default(int), HashCode.Combine);

    public static bool operator ==(ValueObject<T>? left, ValueObject<T>? right)
        => left?.Equals(right as T) ?? right is null;

    public static bool operator !=(ValueObject<T>? left, ValueObject<T>? right)
        => !(left == right);
}
```

## ValueObject example — `HW.Domain/ValueObjects/BlogTitle.cs`
```csharp
using HW.Domain.Abstractions.ValueObjects;
using HW.Domain.Exceptions;

namespace HW.Domain.ValueObjects;

public sealed class BlogTitle : ValueObject<BlogTitle>
{
    public const int MaxLength = 200;

    public string Value { get; }

    private BlogTitle(string value) => Value = value;

    public static BlogTitle Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BadRequestException("Blog title cannot be empty.");

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
            throw new BadRequestException($"Blog title cannot exceed {MaxLength} characters.");

        return new BlogTitle(trimmed);
    }

    public static BlogTitle FromPersistence(string value) => new(value);

    public override IEnumerable<object> GetAtomicValues() { yield return Value; }

    public static implicit operator string(BlogTitle title) => title.Value;
    public override string ToString() => Value;
}
```

## Entity template (new feature)
```csharp
using HW.Domain.Abstractions.Entities;
using HW.Domain.Events.{Feature}s;

namespace HW.Domain.Entities;

public class {Feature} : AggregateRoot, IAuditableEntity, ISoftDeleteEntity
{
    protected {Feature}() { }  // EF Core proxy

    public {Feature}(string prop1, string? prop2)
    {
        Prop1 = prop1;
        Prop2 = prop2;
        RaiseDomainEvent(new {Feature}CreatedDomainEvent(Id, prop1));
    }

    public string Prop1 { get; private set; } = string.Empty;
    public string? Prop2 { get; private set; }

    // IAuditableEntity
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // ISoftDeleteEntity
    public bool? IsDelete { get; set; }

    public void Update(string? prop1, string? prop2)
    {
        if (prop1 is not null) Prop1 = prop1;
        if (prop2 is not null) Prop2 = prop2;
        RaiseDomainEvent(new {Feature}UpdatedDomainEvent(Id, Prop1));
    }

    public void SoftDelete()
    {
        IsDelete = true;
        RaiseDomainEvent(new {Feature}DeletedDomainEvent(Id));
    }
}
```

## Domain event template — `HW.Domain/Events/{Feature}s/`
```csharp
using HW.Domain.Abstractions.Events;

namespace HW.Domain.Events.{Feature}s;

public record {Feature}CreatedDomainEvent(string {Feature}Id, string Prop1) : IDomainEvent;
public record {Feature}UpdatedDomainEvent(string {Feature}Id, string Prop1) : IDomainEvent;
public record {Feature}DeletedDomainEvent(string {Feature}Id) : IDomainEvent;
```

## NotFoundException template — `HW.Domain/Exceptions/{Feature}NotFoundException.cs`
```csharp
namespace HW.Domain.Exceptions;

public class {Feature}NotFoundException : NotFoundException
{
    public {Feature}NotFoundException(string id)
        : base($"{Feature} with id '{id}' was not found.") { }
}
```

## Rules
- Entities extend `AggregateRoot` (not `Entity` directly) to support domain events.
- Use `protected` constructor (not public) — EF Core proxy requirement.
- Properties have `private set` — mutations only through domain methods.
- Domain methods raise events via `RaiseDomainEvent(...)`.
- `SoftDelete()` always raises a deleted event.
- `Update()` only modifies non-null fields (partial update).
- Navigation properties must be `virtual`.
- Foreign keys: `public string? {Related}Id { get; private set; }` (nullable string).
- `ValueObject<T>` subclasses: private constructor, `Create()` factory with validation, `FromPersistence()` for EF conversion.

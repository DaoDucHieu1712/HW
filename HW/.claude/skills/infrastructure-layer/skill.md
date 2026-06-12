# Skill: infrastructure-layer

EFRepository, EFUnitOfWork, ApplicationDbContext, AuditableEntitiesInterceptor, OutboxMessageProcessor — exact implementations.

---

## EFRepository — `HW.Infrastructure/Repositories/EFRepository.cs`
```csharp
using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HW.Infrastructure.Repositories;

public class EFRepository<TEntity> : IEFRepository<TEntity>
    where TEntity : Entity
{
    private readonly ApplicationDbContext _dbContext;

    public EFRepository(ApplicationDbContext dbContext)
        => _dbContext = dbContext;

    public IQueryable<TEntity> FindAll(
        Expression<Func<TEntity, bool>>? predicate = null,
        params Expression<Func<TEntity, object>>[] includeProperties)
    {
        IQueryable<TEntity> items = _dbContext.Set<TEntity>().AsNoTracking();

        if (includeProperties != null)
            foreach (var include in includeProperties)
                items = items.Include(include);

        if (predicate is not null)
            items = items.Where(predicate);

        return items;
    }

    public async Task<TEntity> FindByIdAsync(
        string Id,
        CancellationToken cancellationToken = default,
        params Expression<Func<TEntity, object>>[] includeProperties)
        => await FindAll(null, includeProperties)
            .AsTracking()
            .SingleOrDefaultAsync(x => x.Id == Id, cancellationToken);

    public async Task<TEntity> FindSingleAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default,
        params Expression<Func<TEntity, object>>[] includeProperties)
        => await FindAll(null, includeProperties)
            .AsTracking()
            .SingleOrDefaultAsync(predicate, cancellationToken);

    public void Add(TEntity entity) => _dbContext.Add(entity);
    public void AddRange(List<TEntity> entity) => _dbContext.Set<TEntity>().AddRange(entity);
    public void Update(TEntity entity) => _dbContext.Set<TEntity>().Update(entity);
    public void Remove(TEntity entity) => _dbContext.Set<TEntity>().Remove(entity);
    public void RemoveMultiple(List<TEntity> entities) => _dbContext.Set<TEntity>().RemoveRange(entities);
}
```

---

## EFUnitOfWork — `HW.Infrastructure/EFUnitOfWork.cs`

The UoW wraps mutations in a DB transaction AND converts domain events to outbox messages before commit.

```csharp
using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Entities.Outbox;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace HW.Infrastructure;

public class EFUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _dbContext;

    public EFUnitOfWork(ApplicationDbContext dbContext)
        => _dbContext = dbContext;

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _dbContext.SaveChangesAsync(cancellationToken);

    public async Task ExecuteAsync(Func<Task> action)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                await action();
                ConvertDomainEventsToOutboxMessages();
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private void ConvertDomainEventsToOutboxMessages()
    {
        var aggregates = _dbContext.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var outboxMessages = aggregates
            .SelectMany(a => a.DomainEvents)
            .Select(domainEvent => new OutboxMessage
            {
                // FullName + short assembly name — version-agnostic, resolvable by Type.GetType()
                Type = $"{domainEvent.GetType().FullName}, {domainEvent.GetType().Assembly.GetName().Name}",
                Content = JsonConvert.SerializeObject(domainEvent, domainEvent.GetType(), new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None
                }),
                OccurredOnUtc = DateTimeOffset.UtcNow
            })
            .ToList();

        aggregates.ForEach(a => a.ClearDomainEvents());
        _dbContext.OutboxMessages.AddRange(outboxMessages);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
        => await _dbContext.DisposeAsync();
}
```

---

## ApplicationDbContext — `HW.Infrastructure/ApplicationDbContext.cs`
```csharp
using HW.Domain.Entities;
using HW.Domain.Entities.Outbox;
using HW.Domain.ValueObjects;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HW.Infrastructure;

public class ApplicationDbContext : IdentityDbContext<AppUser>
{
    public ApplicationDbContext() { }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public virtual DbSet<AppUser> AppUsers { get; set; }
    public virtual DbSet<MasterData> MasterDatas { get; set; }
    public virtual DbSet<Blog> Blogs { get; set; }
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }
    // ADD NEW DbSets HERE (alphabetical order, virtual)

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Soft-delete global query filter per entity:
        builder.Entity<Blog>(b =>
        {
            b.HasQueryFilter(x => x.IsDelete != true);
            // Value object conversions:
            b.Property(x => x.Title)
                .HasConversion(t => t.Value, v => BlogTitle.FromPersistence(v))
                .HasMaxLength(BlogTitle.MaxLength);
            b.Property(x => x.Content)
                .HasConversion(c => c.Value, v => BlogContent.FromPersistence(v));
        });

        builder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).IsRequired().HasMaxLength(500);
            b.Property(x => x.Content).IsRequired();
        });
    }
}
```

**When adding a new entity:**
1. Append `public virtual DbSet<{Feature}> {Feature}s { get; set; }` (alphabetical, virtual).
2. Add `builder.Entity<{Feature}>(b => { b.HasQueryFilter(x => x.IsDelete != true); ... })` inside `OnModelCreating` if the entity has soft delete or value objects.

---

## OutboxMessage entity — `HW.Domain/Entities/Outbox/OutboxMessage.cs`
```csharp
namespace HW.Domain.Entities.Outbox;

public class OutboxMessage
{
    public OutboxMessage()
    {
        Id = Guid.NewGuid().ToString();
    }

    public string Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; set; }
    public DateTimeOffset? ProcessedOnUtc { get; set; }
    public int RetryCount { get; set; } = 0;
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? Error { get; set; }
}
```

---

## OutboxMessageProcessor — `HW.Infrastructure/Outbox/OutboxMessageProcessor.cs`

Background service that polls `OutboxMessages`, deserializes domain events, and publishes them via MediatR `IPublisher`.

```csharp
using HW.Application.Abstractions.Events;
using HW.Domain.Abstractions.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HW.Infrastructure.Outbox;

public class OutboxMessageProcessor : BackgroundService
{
    private const int MaxRetries = 5;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxMessageProcessor> _logger;

    public OutboxMessageProcessor(IServiceProvider serviceProvider, ILogger<OutboxMessageProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOutboxMessagesAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "[Outbox] Unexpected error during processing cycle"); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null
                     && m.RetryCount < MaxRetries
                     && (m.NextRetryAt == null || m.NextRetryAt <= DateTimeOffset.UtcNow))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(20)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            try
            {
                var eventType = Type.GetType(message.Type);
                if (eventType is null)
                {
                    message.Error = $"Type not found: {message.Type}";
                    continue;
                }

                var domainEvent = (IDomainEvent)JsonConvert.DeserializeObject(message.Content, eventType)!;
                var wrapperType = typeof(DomainEventWrapper<>).MakeGenericType(eventType);
                var wrapper = (INotification)Activator.CreateInstance(wrapperType, domainEvent)!;

                await publisher.Publish(wrapper, ct);

                message.ProcessedOnUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                if (message.RetryCount >= MaxRetries)
                    message.Error = ex.ToString();
                else
                {
                    var delaySeconds = (int)Math.Pow(2, message.RetryCount) * 5;
                    message.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
```

---

## DomainEventWrapper — `HW.Application/Abstractions/Events/DomainEventWrapper.cs`
```csharp
using HW.Domain.Abstractions.Events;
using MediatR;

namespace HW.Application.Abstractions.Events;

public sealed record DomainEventWrapper<T>(T Event) : INotification where T : IDomainEvent;
```

---

## AuditableEntitiesInterceptor — `HW.Infrastructure/Interceptors/AuditableEntitiesInterceptor.cs`
```csharp
using HW.Domain.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HW.Infrastructure.Interceptors;

public sealed class AuditableEntitiesInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            UpdateAuditableEntities(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void UpdateAuditableEntities(DbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        const string actor = "system"; // TODO: replace with real user from IHttpContextAccessor

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = actor;
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = actor;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = actor;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeleteEntity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.IsDelete ??= false;
        }
    }
}
```

---

## Infrastructure DI — `HW.Infrastructure/DI/ServiceCollectionExtensions.cs`
```csharp
public static void AddInfrastructureServices(this IServiceCollection services)
{
    services.AddHttpContextAccessor();
    services.AddScoped(typeof(IEFRepository<>), typeof(EFRepository<>));
    services.AddScoped<IUnitOfWork, EFUnitOfWork>();
    services.AddHostedService<Outbox.OutboxMessageProcessor>();
}

public static void AddInterceptorDbContext(this IServiceCollection services)
{
    services.AddSingleton<AuditableEntitiesInterceptor>();
}
```

---

## Key rules
- `FindAll()` always uses `.AsNoTracking()` — read-only queries.
- `FindByIdAsync()` / `FindSingleAsync()` use `.AsTracking()` — required for EF to detect mutations.
- `ExecuteAsync()` opens a DB transaction, converts domain events to outbox messages, then commits. Callers must NOT call `SaveChangesAsync()` separately.
- `AuditableEntitiesInterceptor` sets audit fields automatically — services never set `CreatedAt`/`UpdatedAt` directly.
- Outbox processor runs as `BackgroundService` every 10 seconds. Exponential backoff up to `MaxRetries = 5`.

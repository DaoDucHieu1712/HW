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
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

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

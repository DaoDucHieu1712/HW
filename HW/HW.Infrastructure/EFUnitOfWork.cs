using System.Diagnostics;
using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Entities.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HW.Infrastructure;

public class EFUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<EFUnitOfWork> _logger;

    public EFUnitOfWork(ApplicationDbContext dbContext, ILogger<EFUnitOfWork> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var affected = await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("[UnitOfWork] SaveChanges wrote {EntityCount} change(s)", affected);
    }

    public async Task ExecuteAsync(Func<Task> action)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            // The transaction id joins this log line to the ones EF Core writes about the same
            // transaction, and — under a retrying execution strategy — tells a genuine second
            // attempt apart from a duplicate log of the first.
            var transactionId = transaction.TransactionId;
            var stopwatch = Stopwatch.StartNew();

            _logger.LogDebug("[UnitOfWork] Transaction {TransactionId} began", transactionId);

            try
            {
                await action();
                ConvertDomainEventsToOutboxMessages();

                var affected = await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "[UnitOfWork] Transaction {TransactionId} committed {EntityCount} change(s) in {ElapsedMilliseconds} ms",
                    transactionId, affected, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // Logged rather than left to the caller: by the time the exception surfaces, the
                // rollback has already happened, and whether the write was undone is exactly the
                // question being asked when something goes wrong here.
                _logger.LogError(
                    ex,
                    "[UnitOfWork] Transaction {TransactionId} rolled back after {ElapsedMilliseconds} ms",
                    transactionId, stopwatch.ElapsedMilliseconds);

                throw;
            }
        });
    }

    /// <summary>
    /// Moves the domain events raised during this transaction into the outbox, and records what
    /// moved.
    ///
    /// <para>
    /// This is where the Domain layer becomes visible in the log. Entities raise events without any
    /// knowledge of logging — as they should — so the trace is taken at the one point where every
    /// event they raised is collected, and it is written inside the transaction that is about to
    /// commit them, which is what makes it evidence rather than an assumption.
    /// </para>
    /// </summary>
    private void ConvertDomainEventsToOutboxMessages()
    {
        var aggregates = _dbContext.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregates.Count == 0) return;

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

        _logger.LogInformation(
            "[Domain] {EventCount} domain event(s) from {AggregateCount} aggregate(s) queued to the outbox: {EventTypes}",
            outboxMessages.Count,
            aggregates.Count,
            aggregates.SelectMany(a => a.DomainEvents).Select(e => e.GetType().Name).Distinct().ToArray());

        // Aggregate identities at Debug: enough to trace one record's history through the log,
        // but per-event volume that an Information-level sink should not have to carry.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var aggregate in aggregates)
                foreach (var domainEvent in aggregate.DomainEvents)
                    _logger.LogDebug(
                        "[Domain] {AggregateType} {AggregateId} raised {DomainEvent}",
                        aggregate.GetType().Name, aggregate.Id, domainEvent.GetType().Name);
        }

        aggregates.ForEach(a => a.ClearDomainEvents());
        _dbContext.OutboxMessages.AddRange(outboxMessages);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
        => await _dbContext.DisposeAsync();
}

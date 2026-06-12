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
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Outbox] Unexpected error during processing cycle");
            }

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
                    _logger.LogError("[Outbox] Cannot resolve type {Type}", message.Type);
                    continue;
                }

                var domainEvent = (IDomainEvent)JsonConvert.DeserializeObject(message.Content, eventType)!;

                var wrapperType = typeof(DomainEventWrapper<>).MakeGenericType(eventType);
                var wrapper = (INotification)Activator.CreateInstance(wrapperType, domainEvent)!;

                await publisher.Publish(wrapper, ct);

                message.ProcessedOnUtc = DateTimeOffset.UtcNow;
                _logger.LogInformation("[Outbox] Processed {EventType}", eventType.Name);
            }
            catch (Exception ex)
            {
                message.RetryCount++;

                if (message.RetryCount >= MaxRetries)
                {
                    message.Error = ex.ToString();
                    _logger.LogError(ex, "[Outbox] Message {Id} permanently failed after {Max} retries", message.Id, MaxRetries);
                }
                else
                {
                    var delaySeconds = (int)Math.Pow(2, message.RetryCount) * 5;
                    message.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
                    _logger.LogWarning("[Outbox] Message {Id} failed (attempt {Retry}/{Max}), retry in {Delay}s",
                        message.Id, message.RetryCount, MaxRetries, delaySeconds);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }
}

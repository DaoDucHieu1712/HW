using HW.Application.Abstractions.Events;
using HW.Application.Abstractions.Messaging;
using HW.Domain.Abstractions.Events;
using HW.Infrastructure.Messaging;
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
        var brokerBus = scope.ServiceProvider.GetRequiredService<IBrokerBus>();

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null
                     && m.RetryCount < MaxRetries
                     && (m.NextRetryAt == null || m.NextRetryAt <= DateTimeOffset.UtcNow))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(20)
            .ToListAsync(ct);


        if (messages.Count > 0)
            _logger.LogDebug("[Outbox] Dispatching {MessageCount} pending message(s)", messages.Count);

        foreach (var message in messages)
        {
            // The outbox is where a domain event crosses out of the request that raised it, so the
            // request's correlation id is already gone. The message id takes over as the thread to
            // pull on: it is on every line below, and consumers publish under the same id.
            using var messageScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["OutboxMessageId"] = message.Id,
                ["OutboxMessageType"] = message.Type,
                ["OutboxAttempt"] = message.RetryCount + 1
            });

            try
            {
                var eventType = ResolveType(message.Type);
                if (eventType is null)
                {
                    message.Error = $"Type not found: {message.Type}";
                    _logger.LogError("[Outbox] Cannot resolve type {Type}", message.Type);
                    continue;
                }

                var route = RouteFor(eventType);
                var payload = JsonConvert.DeserializeObject(message.Content, eventType)!;

                switch (route)
                {
                    case OutboxRoute.Broker:
                        // The row id travels as the MessageId. If this publish succeeds but the
                        // ProcessedOnUtc write below does not, the retry re-publishes under the same
                        // id and consumers deduplicate it away — which is the whole reason IBrokerBus
                        // takes an id instead of minting one.
                        await brokerBus.PublishAsync(payload, eventType, message.Id, ct);
                        break;

                    case OutboxRoute.DomainEvent:
                        var wrapperType = typeof(DomainEventWrapper<>).MakeGenericType(eventType);
                        var wrapper = (INotification)Activator.CreateInstance(wrapperType, (IDomainEvent)payload)!;

                        await publisher.Publish(wrapper, ct);
                        break;

                    default:
                        // Thrown rather than skipped so it runs through the retry/permanent-failure
                        // bookkeeping below instead of being re-read on every poll forever.
                        throw new InvalidOperationException(
                            $"'{eventType.Name}' is in the outbox but is neither an IDomainEvent nor a " +
                            $"[Message] contract, so there is nowhere to deliver it.");
                }

                message.ProcessedOnUtc = DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "[Outbox] Processed {EventType} -> {Route} (raised {AgeMs} ms earlier)",
                    eventType.Name,
                    route,
                    (long)(DateTimeOffset.UtcNow - message.OccurredOnUtc).TotalMilliseconds);
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

    private enum OutboxRoute
    {
        /// <summary>In-process, via MediatR.</summary>
        DomainEvent,

        /// <summary>Out to the configured broker.</summary>
        Broker,

        /// <summary>Neither — the row cannot be delivered.</summary>
        Unroutable
    }

    /// <summary>
    /// Derives a row's destination from its payload type, so the two kinds of message can share one
    /// table with no discriminator column to keep in sync.
    ///
    /// <para>
    /// <see cref="IDomainEvent"/> is checked first and wins outright. A domain event is an internal
    /// design detail; if one ever picks up a <c>[Message]</c> attribute — by copy-paste, or because
    /// someone wanted to reuse the record — that must not quietly promote it to a public contract
    /// that other services start consuming. Ordering the check this way makes the safe outcome the
    /// default one.
    /// </para>
    /// </summary>
    private static OutboxRoute RouteFor(Type type)
    {
        if (typeof(IDomainEvent).IsAssignableFrom(type)) return OutboxRoute.DomainEvent;

        return type.GetCustomAttributes(typeof(MessageAttribute), inherit: false).Length > 0
            ? OutboxRoute.Broker
            : OutboxRoute.Unroutable;
    }

    // Handles three stored formats:
    //   "BlogCreatedDomainEvent"                                    (old simple name)
    //   "HW.Domain.Events.Blogs.BlogCreatedDomainEvent"             (full name, no assembly)
    //   "HW.Domain.Events.Blogs.BlogCreatedDomainEvent, HW.Domain"  (current format)
    private static Type? ResolveType(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is not null) return type;

        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .FirstOrDefault(t =>
                t.FullName == typeName ||
                $"{t.FullName}, {t.Assembly.GetName().Name}" == typeName ||
                t.Name == typeName);
    }
}

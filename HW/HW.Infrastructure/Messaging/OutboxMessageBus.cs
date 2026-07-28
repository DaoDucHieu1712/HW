using HW.Application.Abstractions.Messaging;
using HW.Domain.Entities.Outbox;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HW.Infrastructure.Messaging;

/// <summary>
/// The <see cref="IMessageBus"/> callers get when <c>Messaging:UseOutbox</c> is on: instead of
/// reaching the broker, a publish writes a row to the same table, in the same transaction, as the
/// data that caused it. <c>OutboxMessageProcessor</c> delivers it afterwards.
///
/// <para>
/// This buys atomicity that a direct publish cannot have. Publishing straight to a broker from inside
/// a command handler is a distributed write with no shared commit: if the handler throws after the
/// publish, <c>TransactionBehavior</c> rolls the database back but the message is already gone and
/// cannot be recalled — other services react to something that never happened. Writing the message
/// through the transaction makes it impossible for one to survive without the other.
/// </para>
///
/// <para>
/// <b>The cost is latency and a changed meaning for <c>PublishAsync</c>.</b> It now returns once the
/// message is durably queued, not once a broker has it; delivery follows on the processor's next
/// poll. Nothing else changes for callers — the seam and the contract are the same.
/// </para>
/// </summary>
internal sealed class OutboxMessageBus : IMessageBus
{
    /// <summary>
    /// Matches <c>EFUnitOfWork.ConvertDomainEventsToOutboxMessages</c> exactly. Both kinds of row are
    /// read back by the same processor, so they must be written by the same serializer — and
    /// <c>TypeNameHandling.None</c> keeps CLR type names out of the payload, since the <c>Type</c>
    /// column is what resolution uses.
    /// </summary>
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<OutboxMessageBus> _logger;

    public OutboxMessageBus(ApplicationDbContext dbContext, ILogger<OutboxMessageBus> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Enlists the message in the current transaction. Nothing is sent here and nothing is saved
    /// here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Call this from a command handler.</b> The row is added to the change tracker and persisted
    /// by whichever <c>SaveChangesAsync</c> commits the unit of work — which for any
    /// <c>IBaseCommand</c> is <c>TransactionBehavior</c> via <c>IUnitOfWork.ExecuteAsync</c>. Publish
    /// from somewhere that never saves — a query handler, say — and the row is discarded with the
    /// scope and the message is silently never sent.
    /// </para>
    /// <para>
    /// Saving here instead would remove that footgun but create a worse one: it would flush every
    /// other pending change on the context at the same time, committing a half-finished unit of work
    /// whenever no transaction happened to be open.
    /// </para>
    /// </remarks>
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : class
    {
        // Validates [Message] at publish time rather than letting the row reach the processor and
        // fail there, where the stack trace no longer points at the caller.
        var topic = MessageSerializer.TopicOf<TMessage>();

        var outboxMessage = new OutboxMessage
        {
            // Same assembly-qualified format the domain-event path writes, so ResolveType handles
            // both without a special case. It is also what routes the row: the processor sees
            // [Message] on this type and sends it to the broker rather than to MediatR — no
            // discriminator column, and nothing to keep in sync with the attribute.
            Type = $"{typeof(TMessage).FullName}, {typeof(TMessage).Assembly.GetName().Name}",
            Content = JsonConvert.SerializeObject(message, typeof(TMessage), SerializerSettings),
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        // The row id becomes the MessageId consumers deduplicate on, and it is assigned now — before
        // the commit — so every later redelivery of this row carries the same one.
        _logger.LogDebug("[Outbox] Queued {MessageId} for topic {Topic}.", outboxMessage.Id, topic);

        return Task.CompletedTask;
    }
}

using HW.Application.Abstractions.Messaging;
using MassTransit;

// MassTransit ships its own MessageContext. Alias ours rather than qualifying it at each use.
using MessageContext = HW.Application.Abstractions.Messaging.MessageContext;

namespace HW.Infrastructure.Messaging.MassTransitAdapter;

/// <summary>
/// Bridges MassTransit's <see cref="IConsumer{TMessage}"/> to this codebase's
/// <see cref="IMessageHandler{TMessage}"/>, so handlers written for the hand-rolled adapter run
/// unmodified under MassTransit.
///
/// <para>
/// One consumer is registered per subscription, closed over the message type at startup. MassTransit
/// resolves it per message from a scope it owns, which is why handlers keep their scoped lifetime
/// here just as they do under <see cref="MessageDispatcher"/>.
/// </para>
///
/// <para>
/// <b><see cref="MessageDispatcher"/> is bypassed on this path.</b> Retry and dead-lettering are
/// MassTransit's, configured from the same <c>MaxDeliveryAttempts</c> and <c>RetryBaseDelayMs</c> so
/// the policy matches — but running our retry loop inside a MassTransit consumer would nest two
/// independent retry mechanisms and multiply the attempts. The observable difference is where failed
/// messages land: MassTransit uses its own <c>{queue}_error</c> queue, not <c>{topic}.dlq</c>.
/// </para>
/// </summary>
internal sealed class MessageHandlerConsumer<TMessage> : IConsumer<TMessage>
    where TMessage : class
{
    private readonly IMessageHandler<TMessage> _handler;

    public MessageHandlerConsumer(IMessageHandler<TMessage> handler) => _handler = handler;

    public Task Consume(ConsumeContext<TMessage> context)
    {
        var messageContext = new MessageContext(
            // MassTransit assigns a MessageId at publish and preserves it across redeliveries, which
            // is the property MessageContext.MessageId promises for deduplication.
            MessageId: context.MessageId?.ToString() ?? string.Empty,

            Topic: MessageSerializer.TopicOf<TMessage>(),

            PartitionKey: context.Headers.Get<string>(MassTransitMessageBus.PartitionKeyHeader),

            // GetRetryAttempt() counts retries, so it is 0 on first delivery; MessageContext counts
            // attempts from 1.
            DeliveryAttempt: context.GetRetryAttempt() + 1,

            PublishedAtUtc: context.SentTime ?? DateTimeOffset.UtcNow,

            Headers: context.Headers
                .GetAll()
                .Where(h => h.Value is not null)
                .ToDictionary(h => h.Key, h => h.Value!.ToString() ?? string.Empty));

        // Exceptions propagate deliberately: that is how a handler asks MassTransit for a retry, and
        // eventually for the error queue — the same contract IMessageHandler documents.
        return _handler.HandleAsync(context.Message, messageContext, context.CancellationToken);
    }
}

using HW.Application.Abstractions.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace HW.Infrastructure.Messaging.MassTransitAdapter;

/// <summary>
/// Publishes through MassTransit's <see cref="IPublishEndpoint"/>.
///
/// <para>
/// Unlike the other two adapters this one does not build a <see cref="MessageEnvelope"/>: MassTransit
/// has its own envelope and would simply wrap ours inside it, leaving two nested sets of ids and
/// timestamps and no way for its own tooling to read either. The message is published as itself and
/// the envelope's fields are mapped onto MassTransit's equivalents — <c>MessageId</c>, <c>SentTime</c>,
/// and headers — which is why <see cref="MessageContext"/> still arrives fully populated at the
/// handler.
/// </para>
///
/// <para>
/// <b>The consequence is that this provider is not wire-compatible with <c>Provider=RabbitMq</c>.</b>
/// Both speak AMQP to the same broker, but with different envelopes and different topologies. Moving
/// between them is a redeploy of publishers and consumers together, not a config flip on a live queue.
/// </para>
/// </summary>
internal sealed class MassTransitMessageBus : IBrokerBus
{
    /// <summary>
    /// Carries <see cref="IPartitionedMessage.PartitionKey"/>. MassTransit has no first-class
    /// partition-key concept on the RabbitMQ transport, and RabbitMQ cannot honour it for ordering
    /// anyway, so it rides as a header to keep <see cref="MessageContext.PartitionKey"/> populated
    /// consistently across providers.
    /// </summary>
    internal const string PartitionKeyHeader = "HW-Partition-Key";

    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitMessageBus> _logger;

    public MassTransitMessageBus(IPublishEndpoint publishEndpoint, ILogger<MassTransitMessageBus> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : class
        => PublishAsync(message, typeof(TMessage), Guid.NewGuid().ToString(), ct);

    public async Task PublishAsync(object message, Type messageType, string messageId, CancellationToken ct)
    {
        // Not used for routing — MassTransit routes on the message type, via the entity-name
        // formatter. Called for its side effect: it throws when [Message] is missing, so the failure
        // mode matches the other providers instead of silently publishing to a type-named exchange.
        var topic = MessageSerializer.TopicOf(messageType);

        var partitionKey = (message as IPartitionedMessage)?.PartitionKey;

        await _publishEndpoint.Publish(message, messageType, context =>
        {
            // MassTransit ids are Guids. Outbox row ids are Guid strings, so this normally takes;
            // if a caller ever supplies something else, MassTransit assigns its own id and the
            // deduplication guarantee quietly weakens — hence the warning rather than a silent skip.
            if (Guid.TryParse(messageId, out var id))
                context.MessageId = id;
            else
                _logger.LogWarning(
                    "[MassTransit] Message id '{MessageId}' is not a Guid; MassTransit will assign its own, " +
                    "so redeliveries of this message will not share an id.", messageId);

            if (partitionKey is not null)
                context.Headers.Set(PartitionKeyHeader, partitionKey);
        }, ct);

        _logger.LogDebug("[MassTransit] Published a {Topic} message.", topic);
    }
}

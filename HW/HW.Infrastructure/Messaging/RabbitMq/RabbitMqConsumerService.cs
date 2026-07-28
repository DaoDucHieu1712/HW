using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Binds one durable queue per registered handler and feeds deliveries to <see cref="MessageDispatcher"/>.
///
/// <para>
/// The queue is named <c>{ConsumerGroup}.{topic}</c>, which is how Kafka's consumer-group semantics
/// are reproduced on AMQP: every instance of this service opens a consumer on the <i>same</i> queue,
/// so the broker hands each message to exactly one of them, and a different group name means a
/// different queue with its own copy of the stream. The queue is durable and not auto-delete, so it
/// keeps accumulating while the service is down — which is also the only reason messages published
/// during a restart survive at all (an AMQP exchange discards anything it cannot route).
/// </para>
/// </summary>
internal sealed class RabbitMqConsumerService : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqConnectionProvider _connections;
    private readonly RabbitMqMessageBus _bus;
    private readonly MessageSubscriptionRegistry _registry;
    private readonly MessageDispatcher _dispatcher;
    private readonly MessagingOptions _options;
    private readonly ILogger<RabbitMqConsumerService> _logger;

    /// <summary>Held for the service's lifetime — disposing the channel cancels its consumers.</summary>
    private IChannel? _channel;

    public RabbitMqConsumerService(
        RabbitMqConnectionProvider connections,
        RabbitMqMessageBus bus,
        MessageSubscriptionRegistry registry,
        MessageDispatcher dispatcher,
        IOptions<MessagingOptions> options,
        ILogger<RabbitMqConsumerService> logger)
    {
        _connections = connections;
        _bus = bus;
        _registry = registry;
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_registry.Subscriptions.Count == 0)
        {
            _logger.LogInformation("[RabbitMQ] No message handlers registered; consumer not started.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);

                // Deliveries arrive on the client's own dispatch threads from here on, and the client
                // recovers the connection, channel, and consumers by itself. This task's only
                // remaining job is to hold the channel open until shutdown.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Almost always a broker that is not up yet. Keep retrying: a consumer that gave up
                // at startup would leave the service silently processing nothing.
                _logger.LogError(ex, "[RabbitMQ] Consumer setup failed; retrying in {Delay}.", ReconnectDelay);

                try { await Task.Delay(ReconnectDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken ct)
    {
        var connection = await _connections.GetAsync(ct);

        _channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(
            exchange: _options.RabbitMq.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        // Per-consumer, not per-channel: the cap applies to each queue's consumer independently.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _options.RabbitMq.PrefetchCount,
            global: false,
            cancellationToken: ct);

        foreach (var subscription in _registry.Subscriptions)
        {
            await BindAndConsumeAsync(subscription, ct);
        }

        _logger.LogInformation("[RabbitMQ] Consuming {Count} topic(s) as group '{Group}': {Topics}.",
            _registry.Subscriptions.Count, _options.ConsumerGroup, string.Join(", ", _registry.Topics));
    }

    private async Task BindAndConsumeAsync(MessageSubscription subscription, CancellationToken ct)
    {
        var queue = QueueNameFor(subscription.Topic);

        await _channel!.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        await _channel.QueueBindAsync(
            queue: queue,
            exchange: _options.RabbitMq.Exchange,
            routingKey: subscription.Topic,
            cancellationToken: ct);

        // The dead-letter queue must exist before anything is dead-lettered into it, for the same
        // reason the main queue must: the exchange drops what it cannot route, and a dead letter
        // dropped on the floor is the one message guaranteed to be worth keeping.
        var deadLetterTopic = _dispatcher.DeadLetterTopicFor(subscription.Topic);
        var deadLetterQueue = QueueNameFor(deadLetterTopic);

        await _channel.QueueDeclareAsync(
            queue: deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        await _channel.QueueBindAsync(
            queue: deadLetterQueue,
            exchange: _options.RabbitMq.Exchange,
            routingKey: deadLetterTopic,
            cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => OnDeliveredAsync(subscription, delivery, ct);

        await _channel.BasicConsumeAsync(
            queue: queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct);
    }

    private async Task OnDeliveredAsync(MessageSubscription subscription, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var envelope = MessageSerializer.TryDeserialize(delivery.Body.Span);

        if (envelope is null)
        {
            // Unparseable: there is no envelope to dead-letter and no attempt count to advance.
            // Redelivering it would loop forever, so drop it and leave a trace for an operator.
            _logger.LogError("[RabbitMQ] Dropped an unreadable message on {Queue} ({Bytes} bytes).",
                QueueNameFor(subscription.Topic), delivery.Body.Length);

            await AckAsync(delivery, ct);
            return;
        }

        var outcome = await _dispatcher.DispatchAsync(envelope, subscription, ct);

        switch (outcome)
        {
            case DispatchOutcome.DeadLetter:
                await _bus.PublishEnvelopeAsync(envelope, _dispatcher.DeadLetterTopicFor(envelope.Topic), CancellationToken.None);
                await AckAsync(delivery, ct);
                break;

            case DispatchOutcome.Abandon:
                // Shutting down. Requeue so another instance — or this one on restart — picks it up.
                await NackAsync(delivery);
                break;

            case DispatchOutcome.Complete:
            default:
                await AckAsync(delivery, ct);
                break;
        }
    }

    private async Task AckAsync(BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        try
        {
            await _channel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // The delivery tag died with its channel, so the broker already considers this message
            // unacknowledged and will redeliver it. Nothing to do but note the duplicate to come.
            _logger.LogWarning(ex, "[RabbitMQ] Could not ack {Tag}; expect a redelivery.", delivery.DeliveryTag);
        }
    }

    private async Task NackAsync(BasicDeliverEventArgs delivery)
    {
        try
        {
            // CancellationToken.None: this runs during shutdown, when the stopping token is already
            // cancelled. Passing it would cancel the nack and strand the message until the
            // connection drops.
            await _channel!.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[RabbitMQ] Could not nack {Tag} during shutdown; the broker will redeliver.",
                delivery.DeliveryTag);
        }
    }

    private string QueueNameFor(string topic) => $"{_options.ConsumerGroup}.{topic}";

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_channel is not null)
        {
            try { await _channel.DisposeAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "[RabbitMQ] Closing the consumer channel failed."); }
        }
    }
}

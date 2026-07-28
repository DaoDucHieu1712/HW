using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.Messaging.Kafka;

/// <summary>
/// Subscribes one consumer group to every registered handler's topic and feeds records to
/// <see cref="MessageDispatcher"/>.
///
/// <para>
/// <c>Messaging:ConsumerGroup</c> maps straight onto Kafka's <c>group.id</c>, which is the semantic
/// the RabbitMQ adapter has to emulate with a shared queue. Kafka splits a topic's partitions across
/// the instances in the group, so scaling past the partition count leaves the extra instances idle —
/// see <c>Messaging:Kafka:DefaultPartitionCount</c>, which cannot be raised later without recreating
/// the topic.
/// </para>
/// </summary>
internal sealed class KafkaConsumerService : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a poll waits before returning null. This is the latency floor for noticing shutdown,
    /// traded against how often an idle consumer wakes a thread.
    /// </summary>
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(1);

    private readonly KafkaMessageBus _bus;
    private readonly MessageSubscriptionRegistry _registry;
    private readonly MessageDispatcher _dispatcher;
    private readonly MessagingOptions _options;
    private readonly ILogger<KafkaConsumerService> _logger;

    public KafkaConsumerService(
        KafkaMessageBus bus,
        MessageSubscriptionRegistry registry,
        MessageDispatcher dispatcher,
        IOptions<MessagingOptions> options,
        ILogger<KafkaConsumerService> logger)
    {
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
            _logger.LogInformation("[Kafka] No message handlers registered; consumer not started.");
            return;
        }

        // BackgroundService runs ExecuteAsync inline until its first await, and everything below
        // blocks. Without this the host would not finish starting until Kafka answered.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureTopicsExistAsync(stoppingToken);
                await ConsumeUntilStoppedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Kafka] Consumer failed; restarting in {Delay}.", ReconnectDelay);

                try { await Task.Delay(ReconnectDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>
    /// Creates any missing topic, including each dead-letter topic.
    ///
    /// Relying on the broker's auto-creation instead would be a trap in two ways: clusters routinely
    /// disable it, and where it is enabled it applies broker defaults — typically one partition,
    /// which silently caps this group at one active consumer forever.
    /// </summary>
    private async Task EnsureTopicsExistAsync(CancellationToken ct)
    {
        var wanted = _registry.Topics
            .SelectMany(topic => new[] { topic, _dispatcher.DeadLetterTopicFor(topic) })
            .ToArray();

        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _options.Kafka.BootstrapServers
        }).Build();

        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(10));

        var existing = metadata.Topics
            .Where(t => t.Error.Code == ErrorCode.NoError)
            .Select(t => t.Topic)
            .ToHashSet(StringComparer.Ordinal);

        var missing = wanted
            .Where(topic => !existing.Contains(topic))
            .Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = _options.Kafka.DefaultPartitionCount,
                ReplicationFactor = _options.Kafka.DefaultReplicationFactor
            })
            .ToArray();

        if (missing.Length == 0) return;

        try
        {
            await admin.CreateTopicsAsync(missing);

            _logger.LogInformation("[Kafka] Created topic(s): {Topics}.",
                string.Join(", ", missing.Select(t => t.Name)));
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r =>
            r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // Another instance won the race. Both wanted the same topology, so this is success.
        }
    }

    private async Task ConsumeUntilStoppedAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.Kafka.BootstrapServers,
            GroupId = _options.ConsumerGroup,

            // Offsets are committed by hand, only after the dispatcher has settled each message.
            // Auto-commit advances on a timer regardless of whether the handler ran, which turns a
            // crash into silent message loss rather than the redelivery handlers are built for.
            EnableAutoCommit = false,

            AutoOffsetReset = ParseOffsetReset(_options.Kafka.AutoOffsetReset)
        };

        using var consumer = new ConsumerBuilder<string?, byte[]>(config)
            .SetErrorHandler((_, error) => _logger.Log(
                error.IsFatal ? LogLevel.Error : LogLevel.Warning,
                "[Kafka] Consumer error: {Reason}", error.Reason))
            .Build();

        consumer.Subscribe(_registry.Topics);

        _logger.LogInformation("[Kafka] Consuming {Count} topic(s) as group '{Group}': {Topics}.",
            _registry.Subscriptions.Count, _options.ConsumerGroup, string.Join(", ", _registry.Topics));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // librdkafka's poll is synchronous, so this parks a thread-pool thread for up to
                // PollTimeout. There is no async form; the timeout keeps the block bounded and gives
                // the loop a chance to observe cancellation.
                var result = consumer.Consume(PollTimeout);

                if (result?.Message is null) continue;

                if (!await HandleAsync(consumer, result, ct)) return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected at shutdown; fall through to Close().
        }
        finally
        {
            // Commits stored offsets and leaves the group explicitly. Skipping this would leave the
            // group waiting out session.timeout.ms before rebalancing — a restart would sit idle for
            // ~10s while the broker held partitions for a member that is already gone.
            consumer.Close();
        }
    }

    /// <summary>Returns false when the loop should stop.</summary>
    private async Task<bool> HandleAsync(IConsumer<string?, byte[]> consumer, ConsumeResult<string?, byte[]> result, CancellationToken ct)
    {
        var envelope = MessageSerializer.TryDeserialize(result.Message.Value);

        if (envelope is null)
        {
            // Nothing to dead-letter and nothing to retry — committing past it is the only way the
            // partition makes progress.
            _logger.LogError("[Kafka] Skipped an unreadable message at {Topic}[{Partition}]@{Offset}.",
                result.Topic, result.Partition.Value, result.Offset.Value);

            Commit(consumer, result);
            return true;
        }

        var subscription = _registry.Find(result.Topic);

        if (subscription is null)
        {
            // Only reachable if a handler was unregistered while its topic stayed subscribed.
            _logger.LogWarning("[Kafka] No handler for topic {Topic}; skipping {MessageId}.",
                result.Topic, envelope.MessageId);

            Commit(consumer, result);
            return true;
        }

        var outcome = await _dispatcher.DispatchAsync(envelope, subscription, ct);

        switch (outcome)
        {
            case DispatchOutcome.DeadLetter:
                // CancellationToken.None: having decided to dead-letter, losing the message to a
                // shutdown that arrives mid-produce would be the worst of both outcomes.
                await _bus.PublishEnvelopeAsync(envelope, _dispatcher.DeadLetterTopicFor(envelope.Topic), CancellationToken.None);
                Commit(consumer, result);
                return true;

            case DispatchOutcome.Abandon:
                // Shutting down with the offset uncommitted, so this record is redelivered to
                // whichever member picks the partition up next.
                return false;

            case DispatchOutcome.Complete:
            default:
                Commit(consumer, result);
                return true;
        }
    }

    private void Commit(IConsumer<string?, byte[]> consumer, ConsumeResult<string?, byte[]> result)
    {
        try
        {
            // Per message, mirroring the RabbitMQ adapter's per-message ack so both providers narrow
            // the duplicate window to one record. It costs a broker round trip per message; a
            // throughput-bound topic would batch instead (StoreOffset plus a commit interval), at the
            // price of replaying up to that interval after a crash.
            consumer.Commit(result);
        }
        catch (KafkaException ex)
        {
            // Typically a rebalance took the partition away mid-handle. The new owner replays from
            // the last committed offset, which is exactly why handlers must be idempotent.
            _logger.LogWarning(ex, "[Kafka] Could not commit {Topic}[{Partition}]@{Offset}; expect a redelivery.",
                result.Topic, result.Partition.Value, result.Offset.Value);
        }
    }

    private AutoOffsetReset ParseOffsetReset(string value)
    {
        if (Enum.TryParse<AutoOffsetReset>(value, ignoreCase: true, out var parsed)) return parsed;

        _logger.LogWarning("[Kafka] '{Value}' is not a valid AutoOffsetReset; using Earliest.", value);
        return AutoOffsetReset.Earliest;
    }
}

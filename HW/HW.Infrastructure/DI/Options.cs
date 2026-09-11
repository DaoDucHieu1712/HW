using System.ComponentModel.DataAnnotations;

namespace HW.Infrastructure.DI;

public static class Options
{
    public record MariaDbRetryOptions
    {
        [Required, Range(5, 20)] public int MaxRetryCount { get; init; }
        [Required, Timestamp] public TimeSpan MaxRetryDelay { get; init; }
        public int[]? ErrorNumbersToAdd { get; init; }
    }

    public record CacheOptions
    {
        /// <summary>Turns caching off wholesale — reads always miss and writes are dropped.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// StackExchange.Redis connection string. Leave empty to run L1-only (no distributed tier).
        /// </summary>
        public string? RedisConnection { get; init; }

        /// <summary>Namespace prefixed to every key, so multiple apps can share one Redis.</summary>
        [Required] public string InstanceName { get; init; } = "hw:";

        /// <summary>TTL applied when a <c>[Cache]</c> attribute does not specify one.</summary>
        [Range(1, 86400)] public int DefaultTtlSeconds { get; init; } = 60;

        /// <summary>
        /// Ceiling on L1 lifetime. L1 is per-instance, so it can miss an invalidation if the Redis
        /// backplane is down; capping it bounds how long that staleness can last.
        /// </summary>
        [Range(1, 3600)] public int L1MaxTtlSeconds { get; init; } = 10;
    }

    /// <summary>
    /// Which message broker adapter <c>AddMessaging</c> wires up. Both real options speak AMQP to the
    /// same RabbitMQ broker; they differ in who owns the envelope and the retry machinery.
    /// </summary>
    public enum MessageBrokerProvider
    {
        /// <summary>No broker. Publishes are logged and dropped; nothing is consumed.</summary>
        None = 0,

        /// <summary>Hand-rolled AMQP adapter. No licence, no dependency.</summary>
        RabbitMq = 1,

        /// <summary>
        /// MassTransit over RabbitMQ. Same <see cref="IMessageBus"/> contract, but MassTransit's own
        /// wire envelope, topology, retry, and error queues — <b>not</b> interchangeable with
        /// <see cref="RabbitMq"/> on a live queue. See pattern 09.
        /// </summary>
        MassTransit = 2
    }

    /// <summary>
    /// Provider-neutral messaging settings, plus the two nested sections.
    ///
    /// <para>
    /// <see cref="RabbitMq"/> holds the connection and is read under <b>both</b> providers, since both
    /// run over the same broker. <see cref="MassTransit"/> adds only what that provider needs on top —
    /// its licence — and is ignored otherwise.
    /// </para>
    /// </summary>
    public record MessagingOptions
    {
        /// <summary>
        /// Selects the adapter at startup. Defaults to <see cref="MessageBrokerProvider.None"/> so a
        /// developer with no broker running gets a working app rather than a connection error.
        /// </summary>
        public MessageBrokerProvider Provider { get; init; } = MessageBrokerProvider.None;

        /// <summary>
        /// Identifies this deployment as a set of competing consumers: each message goes to exactly
        /// one instance in the group, and a second group on the same topic gets its own copy. The
        /// shared queue name is derived from it — <c>{ConsumerGroup}.{topic}</c> — so every instance
        /// consuming that queue competes for the same deliveries. Scaling out means running more
        /// instances under the same group; changing it per instance turns a scaled-out deployment
        /// into N independent consumers that each handle everything.
        /// </summary>
        [Required] public string ConsumerGroup { get; init; } = "hw";

        /// <summary>Total handler attempts before a message is dead-lettered.</summary>
        [Range(1, 10)] public int MaxDeliveryAttempts { get; init; } = 3;

        /// <summary>
        /// First retry delay; doubles per attempt. A retrying message stays unacknowledged and holds
        /// one of its consumer's prefetch slots for the whole schedule, so the default (1s, 2s, 4s)
        /// is kept short deliberately. The hard ceiling is RabbitMQ's <c>consumer_timeout</c> —
        /// 30 minutes by default — past which the broker closes the channel on an unacked delivery
        /// and every message in flight on it is redelivered.
        /// </summary>
        [Range(100, 60_000)] public int RetryBaseDelayMs { get; init; } = 1_000;

        /// <summary>Appended to a topic name to form its dead-letter topic.</summary>
        [Required] public string DeadLetterSuffix { get; init; } = ".dlq";

        /// <summary>
        /// Routes <see cref="IMessageBus"/> through the outbox, so a publish commits atomically with
        /// the data that caused it and is delivered afterwards by <c>OutboxMessageProcessor</c>.
        ///
        /// <para>
        /// On by default: a publish that escapes a rolled-back transaction is a correctness bug, and
        /// the alternative trades that away for latency. Turning it off makes <c>PublishAsync</c> hit
        /// the broker inline — lower latency, no atomicity — which is only appropriate for messages
        /// that are not tied to a database write at all.
        /// </para>
        ///
        /// <para>
        /// Delivery lag is up to one poll of the processor (~10s), so this is not a path for
        /// anything a user is waiting on.
        /// </para>
        /// </summary>
        public bool UseOutbox { get; init; } = true;

        public RabbitMqOptions RabbitMq { get; init; } = new();

        public MassTransitOptions MassTransit { get; init; } = new();
    }

    /// <summary>
    /// Licensing for the MassTransit provider. Connection settings are not repeated here — the
    /// provider reads <see cref="MessagingOptions.RabbitMq"/>, since it runs over the same broker.
    ///
    /// <para>
    /// MassTransit v9 is commercial software and validates a licence at bus configuration. Leave both
    /// properties empty to let it pick up the <c>MT_LICENSE</c> or <c>MT_LICENSE_PATH</c> environment
    /// variable by itself, which is the better fit for deployed environments.
    /// </para>
    /// </summary>
    public record MassTransitOptions
    {
        /// <summary>
        /// The licence key itself. Prefer <see cref="LicensePath"/> or the <c>MT_LICENSE</c>
        /// environment variable — a key pasted into appsettings.json is a secret in source control.
        /// </summary>
        public string? LicenseKey { get; init; }

        /// <summary>Path to a licence file, e.g. <c>./license.txt</c>.</summary>
        public string? LicensePath { get; init; }
    }

    public record RabbitMqOptions
    {
        public string HostName { get; init; } = "localhost";

        [Range(1, 65535)] public int Port { get; init; } = 5672;

        public string UserName { get; init; } = "guest";

        public string Password { get; init; } = "guest";

        public string VirtualHost { get; init; } = "/";

        /// <summary>
        /// The durable topic exchange every message is published to, with the topic as its routing
        /// key. One exchange rather than one per topic keeps the topology flat and lets a consumer
        /// bind to patterns (<c>vocab.*</c>) if it ever needs to.
        /// </summary>
        public string Exchange { get; init; } = "hw.events";

        /// <summary>
        /// Unacknowledged messages allowed per consumer. This is the concurrency limit: messages are
        /// handled one at a time per queue, so raising it only deepens the in-flight buffer.
        /// </summary>
        [Range(1, 1000)] public ushort PrefetchCount { get; init; } = 20;
    }
}

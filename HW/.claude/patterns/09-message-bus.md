# 09 — Message Bus (RabbitMQ / Kafka / MassTransit via Adapter)

`IMessageBus` is one seam with several adapters behind it. Which one runs is a config value, not a
code change. Callers never reference RabbitMQ, Kafka, or MassTransit types.

```
HW.Application/Abstractions/Messaging/        → IMessageBus, IMessageHandler<T>, [Message], MessageContext
HW.Infrastructure/Messaging/                  → MessageEnvelope, MessageDispatcher, NullMessageBus
HW.Infrastructure/Messaging/RabbitMq/         → RabbitMqMessageBus, RabbitMqConsumerService
HW.Infrastructure/Messaging/Kafka/            → KafkaMessageBus, KafkaConsumerService
HW.Infrastructure/Messaging/MassTransitAdapter/ → MassTransitMessageBus, MessageHandlerConsumer<T>
```

| `Messaging:Provider` | Broker | Retry/DLQ by | Licence |
|---|---|---|---|
| `None` | — | — | — |
| `RabbitMq` | RabbitMQ (hand-rolled AMQP) | `MessageDispatcher` | none |
| `Kafka` | Kafka (hand-rolled) | `MessageDispatcher` | none |
| `MassTransit` | RabbitMQ (via MassTransit) | MassTransit | **required** |

Orthogonal to the provider, `Messaging:UseOutbox` (**default on**) decides whether a publish goes to
the broker inline or through the transactional outbox. `IMessageBus` → `OutboxMessageBus` → the table
→ `OutboxMessageProcessor` → `IBrokerBus` → whichever provider above is configured.

`MessageDispatcher` holds the retry/dead-letter policy for the two hand-rolled adapters, which is what
keeps their behaviour identical; only broker I/O lives in the adapters themselves. The MassTransit
provider bypasses it — see below.

## Defining a message

Topic name is explicit, never derived from the type name — renaming the record must not repoint the
stream.

```csharp
[Message("vocab.reviewed")]
public sealed record VocabReviewedMessage(string VocabId, string UserId, int Score) : IPartitionedMessage
{
    // Optional. Kafka keeps same-key messages ordered; RabbitMQ ignores it (see caveats).
    public string? PartitionKey => VocabId;
}
```

## Publishing

```csharp
public sealed class ReviewVocabCommandHandler(IMessageBus bus) : ICommandHandler<ReviewVocabCommand>
{
    public async Task<Unit> Handle(ReviewVocabCommand request, CancellationToken ct)
    {
        // ... domain work ...
        await bus.PublishAsync(new VocabReviewedMessage(vocab.Id, userId, score), ct);
        return Unit.Value;
    }
}
```

With `UseOutbox` on (the default) this **commits atomically with your data** — see below. With it off,
`PublishAsync` returns only once the broker has durably acknowledged the write (publisher confirms on
RabbitMQ, `acks=all` on Kafka), but a publish inside a handler that later throws cannot be recalled.

## The outbox path (`Messaging:UseOutbox`, default on)

`IMessageBus` resolves to `OutboxMessageBus`, which writes the message to the `OutboxMessages` table
**in the caller's transaction** instead of sending it. `OutboxMessageProcessor` — the same one that
has always dispatched domain events — delivers it afterwards.

```
command handler
  ├─ repository.Update(vocab)          ┐
  └─ bus.PublishAsync(new VocabReviewed(...))  ├─ one transaction
                                        ┘
        TransactionBehavior commits ──► row is durable
        OutboxMessageProcessor (≤10s) ──► IBrokerBus ──► RabbitMQ / Kafka
```

Roll back and the row goes with it: the message cannot describe something that never happened. This
is the one guarantee a direct publish cannot give, because a broker and a database have no shared
commit.

**What it costs you:**

- **`PublishAsync` no longer means "the broker has it."** It means durably queued. Delivery follows on
  the processor's next poll — **up to ~10s**. Never put a user-facing round trip behind it.
- **It only works inside a unit of work.** The row is added to the change tracker and saved by
  whatever commits the transaction — `TransactionBehavior` for any `IBaseCommand`. Publish from
  somewhere that never calls `SaveChanges` (a query handler) and the row is dropped with the scope
  and **the message is silently never sent**. Publish from command handlers.
- **`AddMessaging` now needs a registered `DbContext`.** `AddMariaDbConfiguration` must come first in
  `Program.cs`, or resolving `IMessageBus` throws.

Set `UseOutbox: false` to publish inline instead — appropriate only for messages not tied to a
database write, since it trades the atomicity away for latency.

### How a row knows where to go

There is no discriminator column. The processor routes on the payload type it already resolves:

| Payload type | Goes to |
|---|---|
| implements `IDomainEvent` | MediatR, in-process (unchanged behaviour) |
| carries `[Message("topic")]` | the configured broker |
| neither | nothing — logged, retried, then failed permanently |

`IDomainEvent` is checked **first and wins**, even if the type also carries `[Message]`. A domain event
is an internal design detail, and a stray attribute must not quietly promote it into a public contract
other services consume.

### MessageId is the outbox row id

The processor publishes each row through `IBrokerBus`, passing the row's `Id` as the `MessageId`
rather than letting the adapter mint one. This is what makes the deduplication contract in
`IMessageHandler` true: if a publish succeeds but the `ProcessedOnUtc` write fails, the retry
re-publishes **under the same id**, so consumers recognise and discard the duplicate. A fresh `Guid`
per attempt would look like a different message every time.

## Consuming

```csharp
public sealed class VocabReviewedHandler(IRepository<Vocab> repository) : IMessageHandler<VocabReviewedMessage>
{
    public async Task HandleAsync(VocabReviewedMessage message, MessageContext context, CancellationToken ct)
    {
        // Delivery is at-least-once. Deduplicate on context.MessageId, which survives redelivery.
        // ...
    }
}
```

Register it — order relative to `AddMessaging` does not matter:

```csharp
builder.Services.AddMessageHandler<VocabReviewedMessage, VocabReviewedHandler>();
```

Handlers are **scoped**: each message is dispatched in its own DI scope (a fresh one per retry), so
repositories and other scoped services inject normally.

### Handler contract

- **Throwing means "retry me."** Backoff is exponential from `RetryBaseDelayMs`; after
  `MaxDeliveryAttempts` the message goes to `{topic}.dlq` and the consumer moves on.
- **Return normally for messages that can never succeed** (malformed payload, row deleted). Retrying
  them only delays the queue.
- **Be idempotent.** A crash between the handler finishing and the broker recording that fact
  redelivers the message. Key off `context.MessageId`, never an id generated inside the handler.
- **One handler per topic.** A second registration throws at startup. To fan out, use separate topics
  or fan out inside the handler.

## Configuration

```json
"Messaging": {
  "Provider": "None",            // None | RabbitMq | Kafka | MassTransit
  "ConsumerGroup": "hw",         // same value on every instance of a deployment
  "MaxDeliveryAttempts": 3,
  "RetryBaseDelayMs": 1000,
  "DeadLetterSuffix": ".dlq",
  "RabbitMq": { "HostName": "localhost", "Port": 5672, "UserName": "guest", "Password": "guest",
                "VirtualHost": "/", "Exchange": "hw.events", "PrefetchCount": 20 },
  "Kafka":    { "BootstrapServers": "localhost:9092", "AutoOffsetReset": "Earliest",
                "DefaultPartitionCount": 3, "DefaultReplicationFactor": 1 },
  // MassTransit reuses the RabbitMq section for its connection. Leave these empty and use
  // MT_LICENSE / MT_LICENSE_PATH instead — see "The MassTransit provider" below.
  "MassTransit": { "LicenseKey": "", "LicensePath": "" }
}
```

`Provider: None` is the default and means publishes are logged and dropped — the app runs with no
broker installed. Only the selected provider's client is constructed.

`ConsumerGroup` is the competing-consumer identity: every instance sharing it splits the stream
(Kafka `group.id`; RabbitMQ a shared `{group}.{topic}` queue). Scaling out means more instances under
the *same* group — a per-instance group turns a scaled deployment into N consumers that each handle
everything.

## How the vocabulary maps

| Abstraction | RabbitMQ | Kafka |
|---|---|---|
| topic | routing key on the `hw.events` topic exchange | topic |
| consumer group | durable queue `{group}.{topic}` | `group.id` |
| partition key | header only — **no ordering** | partition key — ordering per key |
| settle | `BasicAck` per message | `Commit` per message |
| dead letter | published to `{topic}.dlq`, own queue | produced to `{topic}.dlq` topic |

## Caveats worth knowing before you pick a provider

**RabbitMQ discards messages published to a topic with no bound queue.** This is how AMQP exchanges
work and the adapter cannot hide it. Start the consuming service at least once before publishing, so
its durable queue exists and accumulates while it is down. The publish still *confirms* — the broker
accepted the message, then had nowhere to route it. Kafka retains regardless, for the topic's
retention period.

**Ordering only exists on Kafka.** `IPartitionedMessage` is a preference, not a guarantee, unless the
deployed provider is Kafka. Do not build correctness on it otherwise.

**Retries block their consumer.** Attempts are counted in-process rather than using the brokers'
native machinery (RabbitMQ's nack-requeue loses the count and spins hot; Kafka has no per-message
nack). The cost is head-of-line blocking — a partition on Kafka, a prefetch slot on RabbitMQ — so
keep the backoff schedule short. On Kafka it must stay well inside `max.poll.interval.ms` or the
broker rebalances the partition away mid-retry.

**Kafka topics are auto-created** at consumer startup with `DefaultPartitionCount` partitions, DLQ
topics included. Partition count caps consumer parallelism and cannot be lowered without recreating
the topic. Raise `DefaultReplicationFactor` (3 is typical) for production; it must not exceed the
broker count.

**A publish that throws may still have arrived.** The failure could be in the acknowledgement path.
Combined with redelivery, this is why handlers must be idempotent.

## The MassTransit provider

`Provider: MassTransit` runs MassTransit over RabbitMQ, reading the same `Messaging:RabbitMq`
connection settings. Contracts, handlers, and callers are **unchanged** — `MessageHandlerConsumer<T>`
bridges MassTransit's `IConsumer<T>` to `IMessageHandler<T>`, and `MessageTopicEntityNameFormatter`
makes MassTransit name its exchanges after `[Message]` rather than the CLR type.

It was deliberately kept behind `IMessageBus` rather than adopting MassTransit's own idioms
(`IPublishEndpoint`, `IConsumer<T>`) throughout. That contains the licensing exposure — leaving is one
config value — but it also means the features you'd actually license MassTransit *for* (sagas, state
machines, request/response, its transactional outbox, courier) are **not** reachable through this
seam. If you want those, this adapter is the wrong shape and the Application layer has to take a
direct MassTransit dependency.

### The licence is mandatory and enforced at startup

MassTransit v9 is commercial. **Without a key it throws `ConfigurationException` when the bus is
built** — this is a hard failure, not a warning, and it applies to the in-memory transport too, so it
blocks local runs and tests as much as production:

```
The bus configuration is invalid:
[Failure] License must be specified with SetLicense/SetLicenseLocation or by setting the
MT_LICENSE/MT_LICENSE_PATH environment variables.
```

Orgs under $1M USD annual revenue qualify for a 100% discount, but still register at
[massient.com](https://massient.com/) and install a key. Supply it by:

- `MT_LICENSE` / `MT_LICENSE_PATH` environment variable — **preferred**, MassTransit reads it with no
  code, and the key stays out of source control; or
- `Messaging:MassTransit:LicensePath` → `cfg.SetLicenseLocation(...)`; or
- `Messaging:MassTransit:LicenseKey` → `cfg.SetLicense(...)`. A key in appsettings.json is a secret in
  git — last resort.

v8 is Apache 2.0 with no key, but only receives security patches through end of 2026.

### It is not wire-compatible with `Provider: RabbitMq`

Both speak AMQP to the same broker, but MassTransit uses its own envelope (it would nest ours inside
it, so this adapter publishes the message as itself and maps `MessageContext` onto MassTransit's
`MessageId` / `SentTime` / headers) and its own topology. Switching between `RabbitMq` and
`MassTransit` is a coordinated redeploy of publishers and consumers, **not** a config flip on a live
queue — in-flight messages will not be readable across the change. The two hand-rolled providers are
likewise not wire-compatible with each other; that has never been the claim. What the seam gives you
is that *your code* doesn't change.

### Retry, dead-letters, and registration order

- **`MessageDispatcher` is bypassed.** MassTransit brings its own retry filter and error queues;
  nesting our retry loop inside its would multiply the attempts. `ConfigureRetry` reproduces the
  dispatcher's schedule from the same `MaxDeliveryAttempts` / `RetryBaseDelayMs`, so the *policy*
  matches across providers even though the machinery differs. (MassTransit counts retries where the
  dispatcher counts attempts — hence the `-1`.)
- **Dead letters land in `{queue}_error`**, MassTransit's convention — *not* `{topic}.dlq`. Anything
  monitoring the DLQ needs to know which provider is running.
- **Every `AddMessageHandler<>` must be called before `AddMessaging()`.** MassTransit needs its
  consumers in the container by the time `AddMassTransit` returns, whereas the hand-rolled consumers
  read the registry at runtime and don't care. The registry seals itself on this path, so getting it
  wrong throws at registration instead of silently consuming nothing.

## Failure behaviour

Publishing and consuming fail differently on purpose:

- **Publish fails loudly.** A dropped message is data loss, so an unreachable broker throws and the
  caller decides. This is the opposite of `ICacheService`, which swallows Redis failures because a
  cache miss is safe.
- **The consumer fails quietly and retries.** An unreachable broker at startup logs and retries every
  5s rather than crashing the host; the app serves HTTP either way.

## Local brokers

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management       # UI on http://localhost:15672 (guest/guest)
    ports: ["5672:5672", "15672:15672"]

  kafka:
    image: apache/kafka:3.8.0          # KRaft — no ZooKeeper
    ports: ["9092:9092"]
    environment:
      KAFKA_NODE_ID: 1
      KAFKA_PROCESS_ROLES: broker,controller
      KAFKA_LISTENERS: PLAINTEXT://:9092,CONTROLLER://:9093
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://localhost:9092
      KAFKA_CONTROLLER_QUORUM_VOTERS: 1@localhost:9093
      KAFKA_CONTROLLER_LISTENER_NAMES: CONTROLLER
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
```

Switch providers with `Messaging__Provider=RabbitMq` or `Messaging__Provider=Kafka`; no code changes.

using HW.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.Messaging;

/// <summary>What the adapter should do with a message once the dispatcher is done with it.</summary>
internal enum DispatchOutcome
{
    /// <summary>Handled, or unhandleable. Settle it — ack on RabbitMQ, commit on Kafka.</summary>
    Complete,

    /// <summary>Retries exhausted. Copy it to the dead-letter topic, then settle it.</summary>
    DeadLetter,

    /// <summary>Shutting down mid-flight. Leave it unsettled so the broker redelivers it.</summary>
    Abandon
}

/// <summary>
/// Runs a handler against a message and decides its fate: retry, dead-letter, or done.
///
/// Both adapters delegate here, which is what makes "throwing means retry, N failures means DLQ"
/// mean the same thing on RabbitMQ and Kafka. The brokers' native retry machinery is deliberately
/// unused — RabbitMQ's nack-requeue loses the attempt count and spins hot, and Kafka has no
/// per-message nack at all — so retries are counted in-process, where both can behave identically.
///
/// <para>
/// The cost of in-process retry is head-of-line blocking: a message retrying for
/// <c>MaxDeliveryAttempts</c> holds its consumer. On Kafka that stalls the whole partition; on
/// RabbitMQ it occupies one prefetch slot. This is the intended trade — it preserves ordering and
/// bounds the retry rate — but it means the backoff schedule should stay short.
/// </para>
/// </summary>
internal sealed class MessageDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessagingOptions _options;
    private readonly ILogger<MessageDispatcher> _logger;

    public MessageDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<MessagingOptions> options,
        ILogger<MessageDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Invokes <paramref name="subscription"/>'s handler, retrying with exponential backoff until it
    /// succeeds or attempts run out.
    /// </summary>
    /// <param name="ct">Cancelled on shutdown; in-flight work is abandoned rather than dead-lettered.</param>
    public async Task<DispatchOutcome> DispatchAsync(
        MessageEnvelope envelope,
        MessageSubscription subscription,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= _options.MaxDeliveryAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) return DispatchOutcome.Abandon;

            var context = new MessageContext(
                envelope.MessageId,
                envelope.Topic,
                envelope.PartitionKey,
                attempt,
                envelope.PublishedAtUtc,
                envelope.Headers);

            try
            {
                // A scope per attempt, not per message: a handler that failed partway may have left
                // its DbContext tracking dirty entities, and retrying on that scope would re-save them.
                await using var scope = _scopeFactory.CreateAsyncScope();

                await subscription.Invoke(scope.ServiceProvider, envelope.Payload, context, ct);

                if (attempt > 1)
                    _logger.LogInformation(
                        "[Messaging] {Topic}/{MessageId} succeeded on attempt {Attempt}.",
                        envelope.Topic, envelope.MessageId, attempt);

                return DispatchOutcome.Complete;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown, not failure. Do not burn an attempt or dead-letter it — the broker still
                // holds the message and will hand it to whoever picks the topic up next.
                return DispatchOutcome.Abandon;
            }
            catch (Exception ex)
            {
                if (attempt >= _options.MaxDeliveryAttempts)
                {
                    _logger.LogError(ex,
                        "[Messaging] {Topic}/{MessageId} failed {Attempts} times; dead-lettering to {DlqTopic}.",
                        envelope.Topic, envelope.MessageId, attempt, DeadLetterTopicFor(envelope.Topic));

                    return DispatchOutcome.DeadLetter;
                }

                var delay = BackoffFor(attempt);

                _logger.LogWarning(ex,
                    "[Messaging] {Topic}/{MessageId} failed (attempt {Attempt}/{Max}); retrying in {Delay}.",
                    envelope.Topic, envelope.MessageId, attempt, _options.MaxDeliveryAttempts, delay);

                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    return DispatchOutcome.Abandon;
                }
            }
        }

        return DispatchOutcome.DeadLetter;
    }

    public string DeadLetterTopicFor(string topic) => $"{topic}{_options.DeadLetterSuffix}";

    private TimeSpan BackoffFor(int attempt)
        => TimeSpan.FromMilliseconds(_options.RetryBaseDelayMs * Math.Pow(2, attempt - 1));
}

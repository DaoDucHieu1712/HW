using HW.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace HW.Infrastructure.Messaging;

/// <summary>
/// The adapter used when <c>Messaging:Provider</c> is <c>None</c>: publishes are logged and dropped,
/// and nothing is consumed.
///
/// This exists so <see cref="IMessageBus"/> always resolves. Handlers that take the bus as a
/// dependency stay constructible in unit tests and on a developer machine with no broker running,
/// without every call site having to null-check. It is a real behaviour change, not a stub —
/// messages genuinely go nowhere — so the log line is a warning.
/// </summary>
internal sealed class NullMessageBus : IBrokerBus
{
    private readonly ILogger<NullMessageBus> _logger;

    public NullMessageBus(ILogger<NullMessageBus> logger) => _logger = logger;

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : class
        => PublishAsync(message, typeof(TMessage), Guid.NewGuid().ToString(), ct);

    public Task PublishAsync(object message, Type messageType, string messageId, CancellationToken ct)
    {
        _logger.LogWarning(
            "[Messaging] Dropped a '{Topic}' message: no broker is configured (Messaging:Provider is None).",
            MessageSerializer.TopicOf(messageType));

        return Task.CompletedTask;
    }
}

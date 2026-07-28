using System.Text.Json;
using HW.Application.Abstractions.Messaging;

namespace HW.Infrastructure.Messaging;

/// <summary>
/// One registered handler, reduced to a topic plus a closed delegate.
///
/// The delegate is built in <c>AddMessageHandler&lt;TMessage, THandler&gt;()</c> where both type
/// arguments are still known statically, which keeps deserialization and handler resolution free of
/// runtime reflection on the hot path.
/// </summary>
internal sealed record MessageSubscription(
    string Topic,
    Type MessageType,
    Type HandlerType,
    Func<IServiceProvider, JsonElement, MessageContext, CancellationToken, Task> Invoke);

/// <summary>
/// Every subscription declared at startup. Populated by <c>AddMessageHandler</c>, read once by
/// whichever consumer service the active provider registered.
/// </summary>
internal sealed class MessageSubscriptionRegistry
{
    private readonly Dictionary<string, MessageSubscription> _byTopic = [];

    private bool _sealed;

    public IReadOnlyCollection<MessageSubscription> Subscriptions => _byTopic.Values;

    public IReadOnlyCollection<string> Topics => _byTopic.Keys;

    /// <summary>
    /// Closes the registry to further subscriptions.
    ///
    /// The hand-rolled consumers read this registry when their hosted service starts, so handlers can
    /// be registered in any order relative to <c>AddMessaging</c>. MassTransit cannot: its consumers
    /// must exist in the container by the time <c>AddMassTransit</c> returns. Rather than let a
    /// late-registered handler silently never consume under that one provider, the MassTransit path
    /// seals the registry so the mistake surfaces at registration.
    /// </summary>
    public void Seal() => _sealed = true;

    /// <exception cref="InvalidOperationException">
    /// A second handler claims a topic already taken, or the registry has been sealed. Two handlers
    /// on one topic would compete for the same delivery rather than each receiving a copy, so the
    /// ambiguity is rejected at startup instead of silently dropping one of them.
    /// </exception>
    public void Add(MessageSubscription subscription)
    {
        if (_sealed)
            throw new InvalidOperationException(
                $"'{subscription.HandlerType.Name}' was registered after AddMessaging(), which the MassTransit " +
                $"provider does not support — its consumers are built during AddMessaging and this handler " +
                $"would never receive a message. Move every AddMessageHandler<> call above AddMessaging().");

        if (_byTopic.TryGetValue(subscription.Topic, out var existing))
            throw new InvalidOperationException(
                $"Topic '{subscription.Topic}' is already handled by '{existing.HandlerType.Name}'; " +
                $"'{subscription.HandlerType.Name}' cannot also claim it. One handler per topic — to fan out " +
                $"to several consumers, give each its own topic, or fan out inside the handler.");

        _byTopic.Add(subscription.Topic, subscription);
    }

    public MessageSubscription? Find(string topic)
        => _byTopic.GetValueOrDefault(topic);
}

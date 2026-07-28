using HW.Application.Abstractions.Messaging;
using MassTransit;

namespace HW.Infrastructure.Messaging.MassTransitAdapter;

/// <summary>
/// Names MassTransit's exchanges after the <c>[Message]</c> topic rather than the CLR type.
///
/// Left alone, MassTransit derives the exchange name from the message type's namespace-qualified name
/// (<c>HW.Application.Messages:VocabReviewed</c>). That would make the topic name — the thing the
/// <c>[Message]</c> attribute exists to pin down — depend on where the record happens to live, so
/// moving a namespace would silently repoint the stream. Formatting from the attribute keeps one
/// naming authority across all three providers.
///
/// <para>
/// This aligns the <i>names</i> only. It does not make MassTransit wire-compatible with the
/// hand-rolled RabbitMQ adapter: the topology and the envelope still differ. See pattern 09.
/// </para>
/// </summary>
internal sealed class MessageTopicEntityNameFormatter : IEntityNameFormatter
{
    private readonly IEntityNameFormatter _fallback;

    /// <param name="fallback">
    /// Used for message types with no <c>[Message]</c> attribute. MassTransit publishes its own
    /// internal contracts (fault and receive-fault messages, for one) through this same formatter,
    /// and those will never carry our attribute.
    /// </param>
    public MessageTopicEntityNameFormatter(IEntityNameFormatter fallback) => _fallback = fallback;

    public string FormatEntityName<T>()
    {
        var attribute = typeof(T).GetCustomAttributes(typeof(MessageAttribute), inherit: false)
            .OfType<MessageAttribute>()
            .FirstOrDefault();

        return attribute?.Topic ?? _fallback.FormatEntityName<T>();
    }
}

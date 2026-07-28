using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using HW.Application.Abstractions.Messaging;

namespace HW.Application.Abstractions.Sagas;

/// <summary>
/// Publishes a message whose type is only known at runtime.
///
/// <para>
/// A saga collects its outbound commands as <c>object</c>, because one decision can queue several
/// unrelated contracts. <c>IMessageBus.PublishAsync&lt;TMessage&gt;</c> resolves the topic from the
/// <i>static</i> type argument, so handing it an <c>object</c> would look up <c>[Message]</c> on
/// <c>System.Object</c> and throw. This closes the gap by binding the generic method to the real
/// runtime type.
/// </para>
///
/// <para>
/// The bound delegate is compiled once per message type and cached, so the reflection cost is paid
/// at startup rather than per publish — <c>MakeGenericMethod(...).Invoke(...)</c> on every message
/// would be roughly an order of magnitude slower and would wrap handler exceptions in
/// <c>TargetInvocationException</c>.
/// </para>
/// </summary>
internal static class SagaMessagePublisher
{
    private static readonly MethodInfo PublishDefinition =
        typeof(IMessageBus).GetMethod(nameof(IMessageBus.PublishAsync))!;

    private static readonly ConcurrentDictionary<Type, Func<IMessageBus, object, CancellationToken, Task>> Publishers = new();

    public static Task PublishAsync(IMessageBus bus, object message, CancellationToken ct)
        => Publishers.GetOrAdd(message.GetType(), Compile)(bus, message, ct);

    private static Func<IMessageBus, object, CancellationToken, Task> Compile(Type messageType)
    {
        var bus = Expression.Parameter(typeof(IMessageBus), "bus");
        var message = Expression.Parameter(typeof(object), "message");
        var ct = Expression.Parameter(typeof(CancellationToken), "ct");

        var call = Expression.Call(
            bus,
            PublishDefinition.MakeGenericMethod(messageType),
            Expression.Convert(message, messageType),
            ct);

        return Expression
            .Lambda<Func<IMessageBus, object, CancellationToken, Task>>(call, bus, message, ct)
            .Compile();
    }
}

namespace HW.Application.Abstractions.Sagas;

/// <summary>
/// A message that belongs to a saga conversation. The correlation id is on the contract rather than
/// inferred from a header because it is business data: every participant echoes it back, and it is
/// the only thing that lets a reply arriving minutes later — on a different instance, after a
/// restart — find the stream it belongs to.
///
/// <para>
/// Implementations should also implement <c>IPartitionedMessage</c> returning <see cref="SagaId"/>,
/// so that under Kafka one saga's traffic lands on one partition and stays ordered. It is a
/// preference, not a guarantee (RabbitMQ cannot honour it), which is why the saga's decision methods
/// still tolerate messages arriving out of order.
/// </para>
/// </summary>
public interface ISagaMessage
{
    string SagaId { get; }
}

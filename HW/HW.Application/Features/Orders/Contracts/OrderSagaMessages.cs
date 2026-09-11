using HW.Application.Abstractions.Messaging;
using HW.Application.Abstractions.Sagas;

namespace HW.Application.Features.Orders.Contracts;

/// <summary>
/// The wire contracts of the order saga, split into what the orchestrator <b>asks for</b> and what
/// participants <b>report</b>.
///
/// <para>
/// The distinction is not cosmetic. A command names a service and may be refused; an event states
/// something that already happened and cannot. Keeping them apart is what makes the orchestrator the
/// only component that knows the order of steps — participants know only their own job and the reply
/// they owe.
/// </para>
///
/// <para>
/// Every message carries <c>SagaId</c> and repeats it as its correlation key, and topics are prefixed
/// <c>order.saga.</c> — both so one saga's traffic reads as a single conversation in broker tooling
/// and traces. Neither buys ordering: each topic is its own queue with its own competing consumers,
/// so replies arrive whenever they arrive.
/// </para>
/// </summary>
public static class OrderSagaMessages
{
    // ── Commands: orchestrator → participant ───────────────────────────────────────────────────

    [Message("order.saga.stock.reserve")]
    public sealed record ReserveStock(string SagaId, string Sku, int Quantity)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    /// <summary>Compensation for <see cref="ReserveStock"/>.</summary>
    [Message("order.saga.stock.release")]
    public sealed record ReleaseStock(string SagaId, string ReservationId)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    [Message("order.saga.payment.charge")]
    public sealed record ChargePayment(string SagaId, string CustomerId, decimal Amount)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    // ── Replies: participant → orchestrator ────────────────────────────────────────────────────

    [Message("order.saga.stock.reserved")]
    public sealed record StockReserved(string SagaId, string ReservationId)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    [Message("order.saga.stock.rejected")]
    public sealed record StockRejected(string SagaId, string Reason)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    [Message("order.saga.stock.released")]
    public sealed record StockReleased(string SagaId)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    [Message("order.saga.payment.charged")]
    public sealed record PaymentCharged(string SagaId, string PaymentId)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    [Message("order.saga.payment.failed")]
    public sealed record PaymentFailed(string SagaId, string Reason)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    // ── Outcome: orchestrator → anyone interested ──────────────────────────────────────────────

    /// <summary>
    /// Published once the saga reaches <c>Completed</c>. This — not any individual step — is the
    /// signal that the distributed transaction succeeded, so downstream consumers (invoicing,
    /// notifications) should subscribe here rather than to <c>payment.charged</c>, which can still
    /// be undone.
    /// </summary>
    [Message("order.saga.completed")]
    public sealed record OrderCompleted(string SagaId, string CustomerId, decimal Amount)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }

    /// <summary>Published once the saga has finished undoing itself.</summary>
    [Message("order.saga.cancelled")]
    public sealed record OrderCancelled(string SagaId, string Reason)
        : ISagaMessage, IPartitionedMessage
    {
        public string? PartitionKey => SagaId;
    }
}

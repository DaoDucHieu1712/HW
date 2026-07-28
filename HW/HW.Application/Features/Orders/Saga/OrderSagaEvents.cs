using HW.Domain.Abstractions.Sagas;

namespace HW.Application.Features.Orders.Saga;

/// <summary>
/// The order saga's stream vocabulary. Read top to bottom this is the whole state machine, and a
/// stored stream reads as a narrative: <c>Started → StockReserveSucceeded → PaymentChargeFailed →
/// CompensationStarted → StockReleaseSucceeded → Compensated</c>.
///
/// <para>
/// Each name is in the <b>past tense</b> and each record is closed over the data that made the
/// transition meaningful, because these are read years after they are written and there is no second
/// place to look anything up. <c>StockReserveSucceeded</c> carries the reservation id rather than a
/// bare flag precisely so compensation can be issued from replayed state alone.
/// </para>
/// </summary>
public static class OrderSagaEvents
{
    public sealed record Started(
        string OrderId,
        string CustomerId,
        string Sku,
        int Quantity,
        decimal Amount,
        DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record StockReserveRequested(DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record StockReserveSucceeded(string ReservationId, DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record StockReserveFailed(string Reason, DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record PaymentChargeRequested(DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record PaymentChargeSucceeded(string PaymentId, DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record PaymentChargeFailed(string Reason, DateTimeOffset OccurredAt) : ISagaEvent;

    /// <summary>The pivot: everything after this event is undo work, not forward progress.</summary>
    public sealed record CompensationStarted(string Reason, DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record StockReleaseRequested(DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record StockReleaseSucceeded(DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record OrderConfirmed(DateTimeOffset OccurredAt) : ISagaEvent;

    public sealed record Compensated(string Reason, DateTimeOffset OccurredAt) : ISagaEvent;

    /// <summary>
    /// Compensation could not be completed. Terminal and genuinely inconsistent — this is the event
    /// an alert should be wired to.
    /// </summary>
    public sealed record Failed(string Reason, DateTimeOffset OccurredAt) : ISagaEvent;
}

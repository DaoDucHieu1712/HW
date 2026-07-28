using HW.Domain.Abstractions.Sagas;
using static HW.Application.Features.Orders.Contracts.OrderSagaMessages;
using static HW.Application.Features.Orders.Saga.OrderSagaEvents;

namespace HW.Application.Features.Orders.Saga;

/// <summary>Where the conversation currently is. Derived from the stream, never stored directly.</summary>
public enum OrderSagaStep
{
    NotStarted = 0,
    AwaitingStockReservation = 1,
    AwaitingPayment = 2,
    AwaitingStockRelease = 3,
    Done = 4
}

/// <summary>
/// Orchestrates <c>reserve stock → charge payment → confirm</c> across services that do not share a
/// database, and undoes the completed steps in reverse when a later one fails.
///
/// <para>
/// <b>Why a saga at all.</b> Stock and payment commit in their own databases, so there is no
/// transaction that can span them — you cannot roll back a charge the payment service already
/// committed. A saga replaces atomicity with a weaker but achievable property: the system passes
/// through inconsistent intermediate states, and every step that succeeded before a failure has an
/// explicit compensating action that returns it to consistency. This is eventual consistency by
/// construction, and callers must be told: once <c>ReserveStock</c> is sent, the order is neither
/// placed nor rejected until the saga finishes.
/// </para>
///
/// <para>
/// <b>Why event sourcing here in particular.</b> An orchestrator's whole value is remembering what it
/// already did while waiting on replies that may take minutes and may never come. A state-based row
/// would record only where it is now; the stream records how it got there, which is exactly what
/// compensation needs — <c>StockReserveSucceeded</c> carries the reservation id, so a saga that
/// crashed and rehydrated three restarts later can still issue the right <c>ReleaseStock</c>. It is
/// also the audit log a distributed transaction is always eventually asked to produce.
/// </para>
///
/// <para>
/// <b>Every decision method is a no-op unless the saga is in the matching step.</b> That guard, not
/// the inbox dedup alone, is what makes redelivery and out-of-order arrival safe: a replayed
/// <c>PaymentCharged</c> for a saga already <c>Done</c> is simply ignored rather than charging twice
/// or reopening a closed conversation.
/// </para>
/// </summary>
public sealed class OrderSaga : EventSourcedSaga
{
    /// <summary>
    /// Required by the repository, which constructs an empty instance and replays into it. Not for
    /// application code — start a saga with <see cref="Start"/>.
    /// </summary>
    public OrderSaga() { }

    public string CustomerId { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal Amount { get; private set; }

    public OrderSagaStep Step { get; private set; } = OrderSagaStep.NotStarted;

    /// <summary>Set once stock is held; the handle compensation needs. Null means nothing to undo.</summary>
    public string? ReservationId { get; private set; }

    public string? PaymentId { get; private set; }

    public string? FailureReason { get; private set; }

    public override string CurrentStep => Step.ToString();

    /// <summary>
    /// Begins the conversation: records the intent and asks the stock service to hold the goods.
    /// The saga id doubles as the order id, so a caller holding the order id can query the saga.
    /// </summary>
    public static OrderSaga Start(string orderId, string customerId, string sku, int quantity, decimal amount)
    {
        var saga = new OrderSaga();
        var now = DateTimeOffset.UtcNow;

        saga.Raise(new Started(orderId, customerId, sku, quantity, amount, now));
        saga.Raise(new StockReserveRequested(now));
        saga.Send(new ReserveStock(orderId, sku, quantity));

        return saga;
    }

    /// <summary>Step 1 succeeded. Hold the id needed to undo it, then move money.</summary>
    public void OnStockReserved(string reservationId)
    {
        if (Step != OrderSagaStep.AwaitingStockReservation) return;

        var now = DateTimeOffset.UtcNow;

        Raise(new StockReserveSucceeded(reservationId, now));
        Raise(new PaymentChargeRequested(now));
        Send(new ChargePayment(Id, CustomerId, Amount));
    }

    /// <summary>
    /// Step 1 failed. Nothing has been committed anywhere yet, so the saga ends
    /// <see cref="SagaStatus.Compensated"/> with no compensating command to send — the cheapest
    /// possible failure, and the reason stock is reserved before payment is taken.
    /// </summary>
    public void OnStockRejected(string reason)
    {
        if (Step != OrderSagaStep.AwaitingStockReservation) return;

        var now = DateTimeOffset.UtcNow;

        Raise(new StockReserveFailed(reason, now));
        Raise(new Compensated(reason, now));
        Send(new OrderCancelled(Id, reason));
    }

    /// <summary>Step 2 succeeded — every step has now committed, so the saga completes.</summary>
    public void OnPaymentCharged(string paymentId)
    {
        if (Step != OrderSagaStep.AwaitingPayment) return;

        var now = DateTimeOffset.UtcNow;

        Raise(new PaymentChargeSucceeded(paymentId, now));
        Raise(new OrderConfirmed(now));
        Send(new OrderCompleted(Id, CustomerId, Amount));
    }

    /// <summary>
    /// Step 2 failed with step 1 already committed in another service's database. This is the case
    /// that has no equivalent in a local transaction: the stock is really held, and the only way back
    /// is a new forward action that happens to mean "undo".
    /// </summary>
    public void OnPaymentFailed(string reason)
    {
        if (Step != OrderSagaStep.AwaitingPayment) return;

        var now = DateTimeOffset.UtcNow;

        Raise(new PaymentChargeFailed(reason, now));
        Raise(new CompensationStarted(reason, now));
        Raise(new StockReleaseRequested(now));
        Send(new ReleaseStock(Id, ReservationId!));
    }

    /// <summary>Compensation acknowledged. The system is consistent again, and the order did not happen.</summary>
    public void OnStockReleased()
    {
        if (Step != OrderSagaStep.AwaitingStockRelease) return;

        var reason = FailureReason ?? "unknown";
        var now = DateTimeOffset.UtcNow;

        Raise(new StockReleaseSucceeded(now));
        Raise(new Compensated(reason, now));
        Send(new OrderCancelled(Id, reason));
    }

    /// <summary>
    /// Gives up: a compensating command exhausted its retries and reached the dead-letter queue. The
    /// saga stops rather than retrying forever, and lands in <see cref="SagaStatus.Failed"/> — the
    /// one terminal state that means a human must reconcile the two services by hand.
    /// </summary>
    public void OnCompensationAbandoned(string reason)
    {
        if (IsFinished) return;

        Raise(new Failed(reason, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// The fold. Pure state transition, no side effects, no clock, no id generation — it runs on
    /// every load, once per event ever raised.
    /// </summary>
    protected override void Apply(ISagaEvent sagaEvent)
    {
        switch (sagaEvent)
        {
            case Started e:
                Id = e.OrderId;
                CustomerId = e.CustomerId;
                Sku = e.Sku;
                Quantity = e.Quantity;
                Amount = e.Amount;
                Status = SagaStatus.Running;
                break;

            case StockReserveRequested:
                Step = OrderSagaStep.AwaitingStockReservation;
                break;

            case StockReserveSucceeded e:
                ReservationId = e.ReservationId;
                break;

            case StockReserveFailed e:
                FailureReason = e.Reason;
                break;

            case PaymentChargeRequested:
                Step = OrderSagaStep.AwaitingPayment;
                break;

            case PaymentChargeSucceeded e:
                PaymentId = e.PaymentId;
                break;

            case PaymentChargeFailed e:
                FailureReason = e.Reason;
                break;

            case CompensationStarted:
                Status = SagaStatus.Compensating;
                break;

            case StockReleaseRequested:
                Step = OrderSagaStep.AwaitingStockRelease;
                break;

            case StockReleaseSucceeded:
                ReservationId = null;
                break;

            case OrderConfirmed:
                Status = SagaStatus.Completed;
                Step = OrderSagaStep.Done;
                break;

            case Compensated e:
                FailureReason = e.Reason;
                Status = SagaStatus.Compensated;
                Step = OrderSagaStep.Done;
                break;

            case Failed e:
                FailureReason = e.Reason;
                Status = SagaStatus.Failed;
                Step = OrderSagaStep.Done;
                break;

            // An unknown event means this build is older than the stream it is reading. Throwing
            // beats silently folding a partial history into state the saga then acts on.
            default:
                throw new InvalidOperationException(
                    $"{nameof(OrderSaga)} cannot apply '{sagaEvent.GetType().Name}'.");
        }
    }
}

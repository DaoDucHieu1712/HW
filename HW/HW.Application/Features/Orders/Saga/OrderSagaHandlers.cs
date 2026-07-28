using HW.Application.Abstractions.Messaging;
using HW.Application.Abstractions.Sagas;
using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Sagas;
using Microsoft.Extensions.Logging;
using static HW.Application.Features.Orders.Contracts.OrderSagaMessages;

namespace HW.Application.Features.Orders.Saga;

/// <summary>
/// The orchestrator's ears: one handler per reply the saga waits on. Each is deliberately a single
/// line of real logic, because the branching belongs in <see cref="OrderSaga"/> where it can be
/// read as one state machine — spreading it across handlers is how an orchestration quietly turns
/// back into a choreography nobody can follow.
///
/// <para>
/// Register all four in <c>Program.cs</c> with <c>AddMessageHandler&lt;TMessage, THandler&gt;()</c>.
/// </para>
/// </summary>
public static class OrderSagaHandlers
{
    public sealed class StockReservedHandler : SagaMessageHandler<OrderSaga, StockReserved>
    {
        public StockReservedHandler(
            ISagaRepository sagas,
            IUnitOfWork unitOfWork,
            IMessageBus bus,
            ILogger<StockReservedHandler> logger)
            : base(sagas, unitOfWork, bus, logger) { }

        protected override void Decide(OrderSaga saga, StockReserved message)
            => saga.OnStockReserved(message.ReservationId);
    }

    public sealed class StockRejectedHandler : SagaMessageHandler<OrderSaga, StockRejected>
    {
        public StockRejectedHandler(
            ISagaRepository sagas,
            IUnitOfWork unitOfWork,
            IMessageBus bus,
            ILogger<StockRejectedHandler> logger)
            : base(sagas, unitOfWork, bus, logger) { }

        protected override void Decide(OrderSaga saga, StockRejected message)
            => saga.OnStockRejected(message.Reason);
    }

    public sealed class PaymentChargedHandler : SagaMessageHandler<OrderSaga, PaymentCharged>
    {
        public PaymentChargedHandler(
            ISagaRepository sagas,
            IUnitOfWork unitOfWork,
            IMessageBus bus,
            ILogger<PaymentChargedHandler> logger)
            : base(sagas, unitOfWork, bus, logger) { }

        protected override void Decide(OrderSaga saga, PaymentCharged message)
            => saga.OnPaymentCharged(message.PaymentId);
    }

    /// <summary>
    /// The branch that makes this a saga rather than a pipeline: payment failed after stock was
    /// already committed elsewhere, so the saga turns around and undoes it.
    /// </summary>
    public sealed class PaymentFailedHandler : SagaMessageHandler<OrderSaga, PaymentFailed>
    {
        public PaymentFailedHandler(
            ISagaRepository sagas,
            IUnitOfWork unitOfWork,
            IMessageBus bus,
            ILogger<PaymentFailedHandler> logger)
            : base(sagas, unitOfWork, bus, logger) { }

        protected override void Decide(OrderSaga saga, PaymentFailed message)
            => saga.OnPaymentFailed(message.Reason);
    }

    public sealed class StockReleasedHandler : SagaMessageHandler<OrderSaga, StockReleased>
    {
        public StockReleasedHandler(
            ISagaRepository sagas,
            IUnitOfWork unitOfWork,
            IMessageBus bus,
            ILogger<StockReleasedHandler> logger)
            : base(sagas, unitOfWork, bus, logger) { }

        protected override void Decide(OrderSaga saga, StockReleased message)
            => saga.OnStockReleased();
    }
}

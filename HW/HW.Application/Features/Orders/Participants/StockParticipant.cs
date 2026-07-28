using HW.Application.Abstractions.Messaging;
using HW.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using static HW.Application.Features.Orders.Contracts.OrderSagaMessages;

namespace HW.Application.Features.Orders.Participants;

/// <summary>
/// Stands in for a warehouse service that would live in its own process with its own database.
///
/// <para>
/// <b>It is a stub, but the seam is real.</b> It only ever talks to the saga through the broker, and
/// knows nothing about <c>OrderSaga</c>, the event store, or what happens next — which is exactly the
/// constraint that makes the saga necessary. Replacing it with a genuine remote service is a
/// deployment change, not a code change, because there is no shared type or shared transaction to
/// unpick.
/// </para>
///
/// <para>
/// Outcomes are decided by a fixed rule rather than randomly so the demo is reproducible: quantities
/// above <see cref="StockLimit"/> are rejected, which drives the saga's cheap failure path.
/// </para>
/// </summary>
public static class StockParticipant
{
    private const int StockLimit = 10;

    public sealed class ReserveStockHandler : IMessageHandler<ReserveStock>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMessageBus _bus;
        private readonly ILogger<ReserveStockHandler> _logger;

        public ReserveStockHandler(IUnitOfWork unitOfWork, IMessageBus bus, ILogger<ReserveStockHandler> logger)
        {
            _unitOfWork = unitOfWork;
            _bus = bus;
            _logger = logger;
        }

        /// <summary>
        /// A real implementation would decrement stock and publish the reply in one transaction —
        /// which is precisely what <see cref="IUnitOfWork.ExecuteAsync"/> plus the outbox gives here.
        /// Committing the reservation without the reply escaping would hang the saga forever; the
        /// reply escaping without the reservation would let the saga confirm an order nobody can
        /// ship.
        /// </summary>
        public async Task HandleAsync(ReserveStock message, MessageContext context, CancellationToken ct)
        {
            await _unitOfWork.ExecuteAsync(async () =>
            {
                if (message.Quantity > StockLimit)
                {
                    var reason = $"Only {StockLimit} unit(s) of '{message.Sku}' in stock, {message.Quantity} requested.";
                    _logger.LogWarning("[Stock] Rejecting {SagaId}: {Reason}", message.SagaId, reason);

                    await _bus.PublishAsync(new StockRejected(message.SagaId, reason), ct);
                    return;
                }

                // Derived from the saga id rather than generated, so a redelivery produces the same
                // reservation id instead of a second phantom hold.
                var reservationId = $"RSV-{message.SagaId}";
                _logger.LogInformation("[Stock] Reserved {Qty}x {Sku} for {SagaId} as {ReservationId}",
                    message.Quantity, message.Sku, message.SagaId, reservationId);

                await _bus.PublishAsync(new StockReserved(message.SagaId, reservationId), ct);
            });
        }
    }

    /// <summary>
    /// The compensating action. Note it cannot fail for a business reason — a compensation that can
    /// be refused would leave the saga with nowhere to go, so compensations are designed to be
    /// retryable until they succeed and to be safe to apply twice.
    /// </summary>
    public sealed class ReleaseStockHandler : IMessageHandler<ReleaseStock>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMessageBus _bus;
        private readonly ILogger<ReleaseStockHandler> _logger;

        public ReleaseStockHandler(IUnitOfWork unitOfWork, IMessageBus bus, ILogger<ReleaseStockHandler> logger)
        {
            _unitOfWork = unitOfWork;
            _bus = bus;
            _logger = logger;
        }

        public async Task HandleAsync(ReleaseStock message, MessageContext context, CancellationToken ct)
        {
            await _unitOfWork.ExecuteAsync(async () =>
            {
                _logger.LogInformation("[Stock] Released {ReservationId} for {SagaId}",
                    message.ReservationId, message.SagaId);

                await _bus.PublishAsync(new StockReleased(message.SagaId), ct);
            });
        }
    }
}

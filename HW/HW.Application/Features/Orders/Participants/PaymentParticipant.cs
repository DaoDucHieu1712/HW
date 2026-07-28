using HW.Application.Abstractions.Messaging;
using HW.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using static HW.Application.Features.Orders.Contracts.OrderSagaMessages;

namespace HW.Application.Features.Orders.Participants;

/// <summary>
/// Stands in for a payment service. Same shape as <see cref="StockParticipant"/> — reply-only, no
/// knowledge of the saga — but it is the step that matters most, because it is the one whose success
/// forces a compensation upstream when a *later* step fails, and whose own failure forces one
/// downstream.
///
/// <para>
/// Amounts above <see cref="CreditLimit"/> are declined, which is the switch that drives the
/// interesting path: stock already reserved, payment refused, saga compensates.
/// </para>
/// </summary>
public static class PaymentParticipant
{
    private const decimal CreditLimit = 1_000m;

    public sealed class ChargePaymentHandler : IMessageHandler<ChargePayment>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMessageBus _bus;
        private readonly ILogger<ChargePaymentHandler> _logger;

        public ChargePaymentHandler(IUnitOfWork unitOfWork, IMessageBus bus, ILogger<ChargePaymentHandler> logger)
        {
            _unitOfWork = unitOfWork;
            _bus = bus;
            _logger = logger;
        }

        public async Task HandleAsync(ChargePayment message, MessageContext context, CancellationToken ct)
        {
            await _unitOfWork.ExecuteAsync(async () =>
            {
                if (message.Amount > CreditLimit)
                {
                    var reason = $"Amount {message.Amount:N2} exceeds the {CreditLimit:N2} limit for customer '{message.CustomerId}'.";
                    _logger.LogWarning("[Payment] Declining {SagaId}: {Reason}", message.SagaId, reason);

                    await _bus.PublishAsync(new PaymentFailed(message.SagaId, reason), ct);
                    return;
                }

                // Idempotency key, not a receipt number: the same charge request must never produce
                // two payment ids, or a redelivery bills the customer twice. Real gateways take this
                // value as their own idempotency key for the same reason.
                var paymentId = $"PAY-{message.SagaId}";
                _logger.LogInformation("[Payment] Charged {Amount:N2} for {SagaId} as {PaymentId}",
                    message.Amount, message.SagaId, paymentId);

                await _bus.PublishAsync(new PaymentCharged(message.SagaId, paymentId), ct);
            });
        }
    }
}

using HW.Application.Features.Orders.Participants;
using HW.Application.Features.Orders.Saga;
using HW.Infrastructure.DI;
using static HW.Application.Features.Orders.Contracts.OrderSagaMessages;

namespace HW.Api.DI;

public static class OrderSagaRegistration
{
    /// <summary>
    /// Subscribes the order saga's orchestrator and its two stub participants to their topics.
    ///
    /// <para>
    /// <b>Must be called before <c>AddMessaging</c></b> — under <c>Provider: MassTransit</c> the
    /// subscription registry seals itself during <c>AddMassTransit</c>, and a handler registered
    /// afterwards throws. The hand-rolled adapters do not care, but ordering it correctly here means
    /// switching provider stays a config change.
    /// </para>
    ///
    /// <para>
    /// Orchestrator and participants sit in one process only because this is a demo. Nothing in the
    /// code assumes it: split them across services and the only change is which half of this method
    /// each one calls.
    /// </para>
    /// </summary>
    public static IServiceCollection AddOrderSagaMessaging(this IServiceCollection services)
    {
        // Orchestrator — listens for what participants report.
        services.AddMessageHandler<StockReserved, OrderSagaHandlers.StockReservedHandler>();
        services.AddMessageHandler<StockRejected, OrderSagaHandlers.StockRejectedHandler>();
        services.AddMessageHandler<PaymentCharged, OrderSagaHandlers.PaymentChargedHandler>();
        services.AddMessageHandler<PaymentFailed, OrderSagaHandlers.PaymentFailedHandler>();
        services.AddMessageHandler<StockReleased, OrderSagaHandlers.StockReleasedHandler>();

        // Participants — listen for what the orchestrator asks of them.
        services.AddMessageHandler<ReserveStock, StockParticipant.ReserveStockHandler>();
        services.AddMessageHandler<ReleaseStock, StockParticipant.ReleaseStockHandler>();
        services.AddMessageHandler<ChargePayment, PaymentParticipant.ChargePaymentHandler>();

        return services;
    }
}

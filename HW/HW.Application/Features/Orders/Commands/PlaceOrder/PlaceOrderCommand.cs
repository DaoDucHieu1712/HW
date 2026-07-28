using FluentValidation;
using HW.Application.Abstractions.Messaging;
using HW.Application.Abstractions.Sagas;
using HW.Application.CQRS;
using HW.Application.Features.Orders.Saga;
using HW.Domain.Abstractions.Sagas;
using static HW.Application.Features.Orders.Dtos.OrderDtos;

namespace HW.Application.Features.Orders.Commands.PlaceOrder;

/// <summary>
/// Starts an order saga. Returns as soon as the first step is durably queued — it does not wait for
/// the order to be confirmed, because that depends on services this process cannot see.
/// </summary>
public record PlaceOrderCommand(string CustomerId, string Sku, int Quantity, decimal Amount)
    : ICommand<PlaceOrderResponseDto>;

public class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

/// <summary>
/// The only place a saga is created. Everything afterwards is driven by replies arriving at
/// <c>OrderSagaHandlers</c>.
/// </summary>
/// <remarks>
/// No <c>IUnitOfWork</c> here, per the house rule: <c>TransactionBehavior</c> wraps every
/// <c>IBaseCommand</c>, and both the appended event rows and the outbox row for <c>ReserveStock</c>
/// ride in that transaction. So the first command physically cannot be sent for a saga whose stream
/// failed to commit — the failure mode where a warehouse holds stock for an order that does not
/// exist is designed out rather than handled.
/// </remarks>
public class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, PlaceOrderResponseDto>
{
    private readonly ISagaRepository _sagas;
    private readonly IMessageBus _bus;

    public PlaceOrderCommandHandler(ISagaRepository sagas, IMessageBus bus)
    {
        _sagas = sagas;
        _bus = bus;
    }

    public async Task<PlaceOrderResponseDto> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        var orderId = Guid.NewGuid().ToString();
        var saga = OrderSaga.Start(orderId, request.CustomerId, request.Sku, request.Quantity, request.Amount);

        await _sagas.AppendAsync(saga, ct);

        foreach (var pending in saga.PendingMessages)
            await SagaMessagePublisher.PublishAsync(_bus, pending, ct);

        return new PlaceOrderResponseDto(orderId, saga.CurrentStep);
    }
}

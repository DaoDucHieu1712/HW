public sealed record CreateOrderCommand(Guid CustomerId, IReadOnlyList<OrderLine> Lines)
    : IRequest<Result<Guid>>;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
    }
}

public sealed class CreateOrderCommandHandler(IOrderRepository orders, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        // The rule lives on the aggregate, so every slice gets it.
        var order = Order.Create(command.CustomerId, command.Lines);
        if (order.IsFailure) return Result.Failure<Guid>(order.Error);

        await orders.AddAsync(order.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);   // outbox flushes in the same transaction
        return Result.Success(order.Value.Id);
    }
}

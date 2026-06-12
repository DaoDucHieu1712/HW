using HW.Application.CQRS;
using HW.Domain.Abstractions;
using MediatR;

namespace HW.Application.Behaviors;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork _uow;

    public TransactionBehavior(IUnitOfWork uow) => _uow = uow;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IBaseCommand)
            return await next();

        TResponse response = default!;

        await _uow.ExecuteAsync(async () =>
        {
            response = await next();
        });

        return response;
    }
}

using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities.Sagas;
using HW.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using static HW.Application.Features.Orders.Dtos.OrderDtos;

namespace HW.Application.Features.Orders.Queries.GetOrderSaga;

/// <summary>
/// Reads a saga's current state and, optionally, the stream it was derived from.
/// </summary>
/// <param name="IncludeHistory">
/// Off by default — a stream is unbounded and a hot list endpoint has no business paying for it. Turn
/// it on when you are debugging a stuck saga, where the history is the entire point: it shows every
/// decision, in order, with the data each was made from.
/// </param>
public record GetOrderSagaQuery(string OrderId, bool IncludeHistory = false)
    : IQuery<OrderSagaResponseDto>;

public class GetOrderSagaQueryHandler : IQueryHandler<GetOrderSagaQuery, OrderSagaResponseDto>
{
    private readonly IRepository<SagaInstance> _instances;
    private readonly IRepository<SagaEventRecord> _events;

    public GetOrderSagaQueryHandler(IRepository<SagaInstance> instances, IRepository<SagaEventRecord> events)
    {
        _instances = instances;
        _events = events;
    }

    public async Task<OrderSagaResponseDto> Handle(GetOrderSagaQuery request, CancellationToken ct)
    {
        // Reads the projection rather than replaying: answering "where is this order?" by folding
        // the stream would work but costs one query per saga and scales with its length.
        var instance = await _instances.FindAll(x => x.Id == request.OrderId).FirstOrDefaultAsync(ct)
            ?? throw new OrderSagaNotFoundException(request.OrderId);

        var history = request.IncludeHistory
            ? await _events
                .FindAll(x => x.SagaId == request.OrderId)
                .OrderBy(x => x.Version)
                .Select(x => new SagaEventDto(x.Version, x.Type, x.OccurredOnUtc, x.Content))
                .ToListAsync(ct)
            : [];

        return new OrderSagaResponseDto(
            instance.Id,
            instance.Status,
            instance.CurrentStep,
            instance.Version,
            instance.StartedAtUtc,
            instance.UpdatedAtUtc,
            instance.FinishedAtUtc,
            instance.FailureReason,
            history);
    }
}

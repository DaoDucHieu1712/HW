using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace HW.Application.Features.Vocabs.Queries.GetDailyMission;

public record GetDailyMissionQuery : IQuery<List<VocabDtos.VocabResponseDto>>;

public class GetDailyMissionQueryHandler : IQueryHandler<GetDailyMissionQuery, List<VocabDtos.VocabResponseDto>>
{
    private readonly IEFRepository<Vocab> _repository;

    public GetDailyMissionQueryHandler(IEFRepository<Vocab> repository)
        => _repository = repository;

    public async Task<List<VocabDtos.VocabResponseDto>> Handle(GetDailyMissionQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var due = await _repository
            .FindAll(x => x.ReviewStage < 3 && x.NextReviewAt <= now)
            .OrderBy(x => x.NextReviewAt)
            .ToListAsync(ct);

        return due.Adapt<List<VocabDtos.VocabResponseDto>>();
    }
}

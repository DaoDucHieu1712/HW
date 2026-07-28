using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Enums;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace HW.Application.Features.Vocabs.Queries.GetDailyMission;

public record GetDailyMissionQuery : IQuery<List<VocabDtos.VocabResponseDto>>;

public class GetDailyMissionQueryHandler : IQueryHandler<GetDailyMissionQuery, List<VocabDtos.VocabResponseDto>>
{
    private readonly IRepository<Vocab> _repository;

    public GetDailyMissionQueryHandler(IRepository<Vocab> repository)
        => _repository = repository;

    public async Task<List<VocabDtos.VocabResponseDto>> Handle(GetDailyMissionQuery request, CancellationToken ct)
    {
        var startOfTomorrow = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(1), TimeSpan.Zero);

        var due = await _repository
            .FindAll(x => x.ReviewStage != ReviewStage.Mastered && x.NextReviewAt < startOfTomorrow)
            .OrderBy(x => x.NextReviewAt)
            .ToListAsync(ct);

        return due.Adapt<List<VocabDtos.VocabResponseDto>>();
    }
}

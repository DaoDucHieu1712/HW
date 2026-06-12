using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using Mapster;

namespace HW.Application.Features.Vocabs.Queries.GetVocabs;

public record GetVocabsQuery(string? Search, int PageIndex, int PageSize)
    : IQuery<PagedResult<VocabDtos.VocabResponseDto>>;

public class GetVocabsQueryHandler : IQueryHandler<GetVocabsQuery, PagedResult<VocabDtos.VocabResponseDto>>
{
    private readonly IEFRepository<Vocab> _repository;

    public GetVocabsQueryHandler(IEFRepository<Vocab> repository)
        => _repository = repository;

    public async Task<PagedResult<VocabDtos.VocabResponseDto>> Handle(GetVocabsQuery request, CancellationToken ct)
    {
        var source = _repository.FindAll();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            source = source.Where(x =>
                x.Word.ToLower().Contains(search) ||
                (x.Meaning != null && x.Meaning.ToLower().Contains(search)));
        }

        var paged = await PagedResult<Vocab>.CreateAsync(source, request.PageIndex, request.PageSize);
        return paged.Adapt<PagedResult<VocabDtos.VocabResponseDto>>();
    }
}

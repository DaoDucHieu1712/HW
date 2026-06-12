using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using Mapster;

namespace HW.Application.Features.Vocabs.Queries.GetVocabById;

public record GetVocabByIdQuery(string Id) : IQuery<VocabDtos.VocabResponseDto>;

public class GetVocabByIdQueryHandler : IQueryHandler<GetVocabByIdQuery, VocabDtos.VocabResponseDto>
{
    private readonly IEFRepository<Vocab> _repository;

    public GetVocabByIdQueryHandler(IEFRepository<Vocab> repository)
        => _repository = repository;

    public async Task<VocabDtos.VocabResponseDto> Handle(GetVocabByIdQuery request, CancellationToken ct)
    {
        var vocab = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new VocabNotFoundException(request.Id);

        return vocab.Adapt<VocabDtos.VocabResponseDto>();
    }
}

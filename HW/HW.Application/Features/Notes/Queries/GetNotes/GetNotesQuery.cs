using HW.Application.CQRS;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using Mapster;
using static HW.Application.Features.Notes.Dtos.NoteDtos;

namespace HW.Application.Features.Notes.Queries.GetNotes;

public record GetNotesQuery(string? FolderId, string? Search, int PageIndex = 1, int PageSize = 20) : IQuery<PagedResult<NoteResponseDto>>;

public class GetNotesQueryHandler : IQueryHandler<GetNotesQuery, PagedResult<NoteResponseDto>>
{
    private readonly IRepository<Note> _repository;

    public GetNotesQueryHandler(IRepository<Note> repository)
        => _repository = repository;

    public async Task<PagedResult<NoteResponseDto>> Handle(GetNotesQuery request, CancellationToken ct)
    {
        var source = _repository.FindAll(n => n.FolderId == request.FolderId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            source = source.Where(n => n.Title != null && n.Title.ToLower().Contains(search));
        }

        var paged = await PagedResult<Note>.CreateAsync(source, request.PageIndex, request.PageSize);
        return paged.Adapt<PagedResult<NoteResponseDto>>();
    }
}

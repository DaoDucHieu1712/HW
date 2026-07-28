using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using Mapster;
using static HW.Application.Features.Notes.Dtos.NoteDtos;

namespace HW.Application.Features.Notes.Queries.GetNoteById;

public record GetNoteByIdQuery(string Id) : IQuery<NoteResponseDto>;

public class GetNoteByIdQueryHandler : IQueryHandler<GetNoteByIdQuery, NoteResponseDto>
{
    private readonly IRepository<Note> _repository;

    public GetNoteByIdQueryHandler(IRepository<Note> repository)
        => _repository = repository;

    public async Task<NoteResponseDto> Handle(GetNoteByIdQuery request, CancellationToken ct)
    {
        var note = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new NoteNotFoundException(request.Id);

        return note.Adapt<NoteResponseDto>();
    }
}

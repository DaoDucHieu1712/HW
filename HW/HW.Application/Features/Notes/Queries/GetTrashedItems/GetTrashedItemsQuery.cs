using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static HW.Application.Features.Notes.Dtos.NoteDtos;

namespace HW.Application.Features.Notes.Queries.GetTrashedItems;

public record GetTrashedItemsQuery() : IQuery<TrashedItemsResponseDto>;

public class GetTrashedItemsQueryHandler : IQueryHandler<GetTrashedItemsQuery, TrashedItemsResponseDto>
{
    private readonly IRepository<Folder> _folderRepository;
    private readonly IRepository<Note> _noteRepository;

    public GetTrashedItemsQueryHandler(IRepository<Folder> folderRepository, IRepository<Note> noteRepository)
    {
        _folderRepository = folderRepository;
        _noteRepository = noteRepository;
    }

    public async Task<TrashedItemsResponseDto> Handle(GetTrashedItemsQuery request, CancellationToken ct)
    {
        var folders = await _folderRepository.FindAllIgnoreFilters(f => f.IsDelete == true)
            .Select(f => new TrashedFolderDto(f.Id, f.Name, f.ParentId, f.UpdatedAt))
            .ToListAsync(ct);

        var notes = await _noteRepository.FindAllIgnoreFilters(n => n.IsDelete == true)
            .Select(n => new TrashedNoteDto(n.Id, n.Title, n.FolderId, n.UpdatedAt))
            .ToListAsync(ct);

        return new TrashedItemsResponseDto(folders, notes);
    }
}

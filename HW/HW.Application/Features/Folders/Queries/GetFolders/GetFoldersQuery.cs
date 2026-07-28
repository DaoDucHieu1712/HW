using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static HW.Application.Features.Folders.Dtos.FolderDtos;

namespace HW.Application.Features.Folders.Queries.GetFolders;

public record GetFoldersQuery() : IQuery<List<FolderResponseDto>>;

public class GetFoldersQueryHandler : IQueryHandler<GetFoldersQuery, List<FolderResponseDto>>
{
    private readonly IRepository<Folder> _repository;

    public GetFoldersQueryHandler(IRepository<Folder> repository)
        => _repository = repository;

    public async Task<List<FolderResponseDto>> Handle(GetFoldersQuery request, CancellationToken ct)
    {
        var all = await _repository.FindAll().ToListAsync(ct);
        return BuildTree(all, null);
    }

    private static List<FolderResponseDto> BuildTree(List<Folder> all, string? parentId)
        => all
            .Where(f => f.ParentId == parentId)
            .Select(f => new FolderResponseDto(
                f.Id, f.Name, f.ParentId, f.Icon,
                f.CreatedAt, f.UpdatedAt,
                BuildTree(all, f.Id)))
            .ToList();
}

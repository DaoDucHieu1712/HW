using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using static HW.Application.Features.Folders.Dtos.FolderDtos;

namespace HW.Application.Features.Folders.Queries.GetFolderById;

public record GetFolderByIdQuery(string Id) : IQuery<FolderResponseDto>;

public class GetFolderByIdQueryHandler : IQueryHandler<GetFolderByIdQuery, FolderResponseDto>
{
    private readonly IRepository<Folder> _repository;

    public GetFolderByIdQueryHandler(IRepository<Folder> repository)
        => _repository = repository;

    public async Task<FolderResponseDto> Handle(GetFolderByIdQuery request, CancellationToken ct)
    {
        var folder = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new FolderNotFoundException(request.Id);

        return new FolderResponseDto(folder.Id, folder.Name, folder.ParentId, folder.Icon, folder.CreatedAt, folder.UpdatedAt, new());
    }
}

namespace HW.Application.Features.Folders.Dtos;

public static class FolderDtos
{
    public record FolderResponseDto(
        string Id,
        string Name,
        string? ParentId,
        string? Icon,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? UpdatedAt,
        List<FolderResponseDto> Children);

    public record CreateFolderRequestDto(string Name, string? ParentId, string? Icon);
    public record UpdateFolderRequestDto(string? Name, string? Icon);
    public record MoveFolderRequestDto(string? NewParentId);
}

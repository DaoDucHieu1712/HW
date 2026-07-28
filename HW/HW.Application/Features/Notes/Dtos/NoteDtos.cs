namespace HW.Application.Features.Notes.Dtos;

public static class NoteDtos
{
    public record NoteResponseDto(
        string Id,
        string? Title,
        string? Content,
        string? FolderId,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? UpdatedAt);

    public record TrashedFolderDto(string Id, string Name, string? ParentId, DateTimeOffset? UpdatedAt);
    public record TrashedNoteDto(string Id, string? Title, string? FolderId, DateTimeOffset? UpdatedAt);
    public record TrashedItemsResponseDto(List<TrashedFolderDto> Folders, List<TrashedNoteDto> Notes);

    public record CreateNoteRequestDto(string? Title, string? Content, string? FolderId);
    public record UpdateNoteRequestDto(string? Title, string? Content);
    public record MoveNoteRequestDto(string? FolderId);
}

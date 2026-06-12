using HW.Domain.Abstractions.Entities;

namespace HW.Domain.Entities;

public class Note : Entity, IAuditableEntity, ISoftDeleteEntity
{
    public Note(string? title, string? description, string? imageUrl, string? tag, string? noteContent, string? type)
    {
        Title = title;
        Description = description;
        ImageUrl = imageUrl;
        Tag = tag;
        NoteContent = noteContent;
        Type = type;
    }

    public Note() { }


    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Tag { get; set; }
    public string? NoteContent { get; set; }
    public string? Type { get; set; }  //E - EN; W - WORKOUT

    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }
}

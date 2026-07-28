using HW.Domain.Abstractions.Entities;
using HW.Domain.Enums;
using HW.Domain.Events.Notes;

namespace HW.Domain.Entities;

public class Note : AggregateRoot, IAuditableEntity, ISoftDeleteEntity
{
    protected Note() { }

    public Note(string? title, string? description, string? imageUrl, string? tag, string? content, NoteType type, string? folderId = null)
    {
        Title = title;
        Description = description;
        ImageUrl = imageUrl;
        Tag = tag;
        Content = content;
        Type = type;
        FolderId = folderId;
        RaiseDomainEvent(new NoteCreatedDomainEvent(Id, title, type));
    }

    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? Tag { get; private set; }
    public string? Content { get; private set; }
    public NoteType Type { get; private set; }
    public string? FolderId { get; private set; }
    public virtual Folder? Folder { get; private set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }

    public void Update(string? title, string? description, string? imageUrl, string? tag, string? content)
    {
        if (title is not null) Title = title;
        if (description is not null) Description = description;
        if (imageUrl is not null) ImageUrl = imageUrl;
        if (tag is not null) Tag = tag;
        if (content is not null) Content = content;
        RaiseDomainEvent(new NoteUpdatedDomainEvent(Id, Title));
    }

    public void Move(string? folderId)
    {
        FolderId = folderId;
    }

    public void SoftDelete()
    {
        IsDelete = true;
        RaiseDomainEvent(new NoteDeletedDomainEvent(Id));
    }

    public void Restore()
    {
        IsDelete = false;
        RaiseDomainEvent(new NoteRestoredDomainEvent(Id));
    }
}

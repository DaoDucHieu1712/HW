using HW.Domain.Abstractions.Entities;
using HW.Domain.Events.Folders;

namespace HW.Domain.Entities;

public class Folder : AggregateRoot, IAuditableEntity, ISoftDeleteEntity
{
    protected Folder() { }

    public Folder(string name, string? parentId, string? icon)
    {
        Name = name;
        ParentId = parentId;
        Icon = icon;
        RaiseDomainEvent(new FolderCreatedDomainEvent(Id, name, parentId));
    }

    public string Name { get; private set; } = null!;
    public string? ParentId { get; private set; }
    public string? Icon { get; private set; }
    public virtual Folder? Parent { get; private set; }
    public virtual ICollection<Folder> Children { get; private set; } = new List<Folder>();
    public virtual ICollection<Note> Notes { get; private set; } = new List<Note>();

    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }

    public void Update(string? name, string? icon)
    {
        if (name is not null) Name = name;
        if (icon is not null) Icon = icon;
        RaiseDomainEvent(new FolderUpdatedDomainEvent(Id, Name));
    }

    public void Move(string? newParentId)
    {
        ParentId = newParentId;
        RaiseDomainEvent(new FolderMovedDomainEvent(Id, newParentId));
    }

    public void SoftDelete()
    {
        IsDelete = true;
        RaiseDomainEvent(new FolderDeletedDomainEvent(Id));
    }

    public void Restore()
    {
        IsDelete = false;
        RaiseDomainEvent(new FolderRestoredDomainEvent(Id));
    }
}

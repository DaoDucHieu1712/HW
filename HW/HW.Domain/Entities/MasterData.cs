using HW.Domain.Abstractions.Entities;

namespace HW.Domain.Entities;

public class MasterData : Entity, IAuditableEntity, ISoftDeleteEntity
{
    public MasterData(string type, string code, string name, 
        string? description, int order, string? parentId, bool isActive)
    {
        Id = Guid.NewGuid().ToString();
        Type = type;
        Code = code;
        Name = name;
        Description = description;
        Order = order;
        ParentId = parentId;
        IsActive = isActive;
    }

    public string Type { get; set; }
    public string Code { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public int Order { get; set; }
    public string? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }
}

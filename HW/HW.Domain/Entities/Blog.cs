using HW.Domain.Abstractions.Entities;

namespace HW.Domain.Entities;

public class Blog : Entity, IAuditableEntity, ISoftDeleteEntity
{
    public Blog(string? title, string? content)
    {
        Id = Guid.NewGuid().ToString();
        Title = title;
        Content = content;
    }

    public Blog()
    {
    }

    public string? Title { get; set; }
    public string? Content { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }
}

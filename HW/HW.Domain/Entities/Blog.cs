using HW.Domain.Abstractions.Entities;
using HW.Domain.Events.Blogs;
using HW.Domain.ValueObjects;

namespace HW.Domain.Entities;

public class Blog : AggregateRoot, IAuditableEntity, ISoftDeleteEntity
{
    protected Blog()
    {
        Title = null!;
        Content = null!;
    }

    public Blog(BlogTitle title, BlogContent content)
    {
        Title = title;
        Content = content;
        RaiseDomainEvent(new BlogCreatedDomainEvent(Id, title.Value, content.Value));
    }

    public BlogTitle Title { get; private set; }
    public BlogContent Content { get; private set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }

    public void Update(BlogTitle? title, BlogContent? content)
    {
        if (title is not null) Title = title;
        if (content is not null) Content = content;
        RaiseDomainEvent(new BlogUpdatedDomainEvent(Id, Title.Value, Content.Value));
    }

    public void SoftDelete()
    {
        IsDelete = true;
        RaiseDomainEvent(new BlogDeletedDomainEvent(Id));
    }
}

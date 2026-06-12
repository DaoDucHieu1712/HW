using HW.Domain.Abstractions.Events;

namespace HW.Domain.Events.Blogs;

public record BlogDeletedDomainEvent(string BlogId) : IDomainEvent;

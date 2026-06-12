using HW.Domain.Abstractions.Events;

namespace HW.Domain.Events.Blogs;

public record BlogCreatedDomainEvent(string BlogId, string? Title, string? Content) : IDomainEvent;

using HW.Domain.Abstractions.Events;

namespace HW.Domain.Events.Blogs;

public record BlogUpdatedDomainEvent(string BlogId, string? Title, string? Content) : IDomainEvent;

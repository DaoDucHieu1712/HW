using HW.Application.Abstractions.Events;
using HW.Domain.Events.Blogs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HW.Application.Features.Blogs.Events;

public class BlogCreatedDomainEventHandler : INotificationHandler<DomainEventWrapper<BlogCreatedDomainEvent>>
{
    private readonly ILogger<BlogCreatedDomainEventHandler> _logger;

    public BlogCreatedDomainEventHandler(ILogger<BlogCreatedDomainEventHandler> logger)
        => _logger = logger;

    public Task Handle(DomainEventWrapper<BlogCreatedDomainEvent> notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[DomainEvent] Blog created — Id: {BlogId}, Title: {Title}",
            notification.Event.BlogId,
            notification.Event.Title);

        return Task.CompletedTask;
    }
}

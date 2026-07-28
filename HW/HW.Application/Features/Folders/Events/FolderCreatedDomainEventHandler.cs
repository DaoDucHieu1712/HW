using HW.Application.Abstractions.Events;
using HW.Domain.Events.Folders;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HW.Application.Features.Folders.Events;

public class FolderCreatedDomainEventHandler : INotificationHandler<DomainEventWrapper<FolderCreatedDomainEvent>>
{
    private readonly ILogger<FolderCreatedDomainEventHandler> _logger;

    public FolderCreatedDomainEventHandler(ILogger<FolderCreatedDomainEventHandler> logger)
        => _logger = logger;

    public Task Handle(DomainEventWrapper<FolderCreatedDomainEvent> notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[DomainEvent] Folder created — Id: {FolderId}, Name: {Name}",
            notification.Event.FolderId,
            notification.Event.Name);
        return Task.CompletedTask;
    }
}

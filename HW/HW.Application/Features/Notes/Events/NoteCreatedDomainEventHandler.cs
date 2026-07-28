using HW.Application.Abstractions.Events;
using HW.Domain.Events.Notes;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HW.Application.Features.Notes.Events;

public class NoteCreatedDomainEventHandler : INotificationHandler<DomainEventWrapper<NoteCreatedDomainEvent>>
{
    private readonly ILogger<NoteCreatedDomainEventHandler> _logger;

    public NoteCreatedDomainEventHandler(ILogger<NoteCreatedDomainEventHandler> logger)
        => _logger = logger;

    public Task Handle(DomainEventWrapper<NoteCreatedDomainEvent> notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[DomainEvent] Note created — Id: {NoteId}, Title: {Title}",
            notification.Event.NoteId,
            notification.Event.Title);
        return Task.CompletedTask;
    }
}

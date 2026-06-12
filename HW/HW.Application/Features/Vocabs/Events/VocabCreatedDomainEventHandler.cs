using HW.Application.Abstractions.Events;
using HW.Domain.Events.Vocabs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HW.Application.Features.Vocabs.Events;

public class VocabCreatedDomainEventHandler
    : INotificationHandler<DomainEventWrapper<VocabCreatedDomainEvent>>
{
    private readonly ILogger<VocabCreatedDomainEventHandler> _logger;

    public VocabCreatedDomainEventHandler(ILogger<VocabCreatedDomainEventHandler> logger)
        => _logger = logger;

    public Task Handle(DomainEventWrapper<VocabCreatedDomainEvent> notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[DomainEvent] Vocab created — Id: {VocabId}, Word: {Word}",
            notification.Event.VocabId,
            notification.Event.Word);

        return Task.CompletedTask;
    }
}

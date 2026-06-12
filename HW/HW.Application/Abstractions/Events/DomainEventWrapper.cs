using HW.Domain.Abstractions.Events;
using MediatR;

namespace HW.Application.Abstractions.Events;

public sealed record DomainEventWrapper<T>(T Event) : INotification where T : IDomainEvent;

using HW.Domain.Abstractions.Events;

namespace HW.Domain.Events.UserFitnessProfiles;

public record UserFitnessProfileCreatedDomainEvent(string ProfileId) : IDomainEvent;
public record UserFitnessProfileUpdatedDomainEvent(string ProfileId) : IDomainEvent;
public record UserFitnessProfileDeletedDomainEvent(string ProfileId) : IDomainEvent;

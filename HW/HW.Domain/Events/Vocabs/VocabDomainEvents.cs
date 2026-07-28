using HW.Domain.Abstractions.Events;
using HW.Domain.Enums;

namespace HW.Domain.Events.Vocabs;

public record VocabCreatedDomainEvent(string VocabId, string Word) : IDomainEvent;
public record VocabUpdatedDomainEvent(string VocabId, string Word) : IDomainEvent;
public record VocabDeletedDomainEvent(string VocabId) : IDomainEvent;
public record VocabReviewedDomainEvent(string VocabId, ReviewStage NewStage, DateTimeOffset? NextReviewAt) : IDomainEvent;

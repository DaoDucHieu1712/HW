using HW.Domain.Abstractions.Events;

namespace HW.Domain.Events.Vocabs;

public record VocabCreatedDomainEvent(string VocabId, string Word) : IDomainEvent;
public record VocabUpdatedDomainEvent(string VocabId, string Word) : IDomainEvent;
public record VocabDeletedDomainEvent(string VocabId) : IDomainEvent;
public record VocabReviewedDomainEvent(string VocabId, int NewStage, DateTimeOffset? NextReviewAt) : IDomainEvent;

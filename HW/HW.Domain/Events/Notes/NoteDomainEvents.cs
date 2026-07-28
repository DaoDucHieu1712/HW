using HW.Domain.Abstractions.Events;
using HW.Domain.Enums;

namespace HW.Domain.Events.Notes;

public record NoteCreatedDomainEvent(string NoteId, string? Title, NoteType Type) : IDomainEvent;
public record NoteUpdatedDomainEvent(string NoteId, string? Title) : IDomainEvent;
public record NoteDeletedDomainEvent(string NoteId) : IDomainEvent;
public record NoteRestoredDomainEvent(string NoteId) : IDomainEvent;

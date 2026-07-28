using HW.Domain.Abstractions.Events;

namespace HW.Domain.Events.Folders;

public record FolderCreatedDomainEvent(string FolderId, string Name, string? ParentId) : IDomainEvent;
public record FolderUpdatedDomainEvent(string FolderId, string Name) : IDomainEvent;
public record FolderDeletedDomainEvent(string FolderId) : IDomainEvent;
public record FolderMovedDomainEvent(string FolderId, string? NewParentId) : IDomainEvent;
public record FolderRestoredDomainEvent(string FolderId) : IDomainEvent;

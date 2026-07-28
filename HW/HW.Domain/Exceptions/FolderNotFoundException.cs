namespace HW.Domain.Exceptions;

public sealed class FolderNotFoundException(string id)
    : NotFoundException($"Folder with id '{id}' was not found.");

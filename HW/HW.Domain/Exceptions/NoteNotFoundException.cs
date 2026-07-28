namespace HW.Domain.Exceptions;

public sealed class NoteNotFoundException(string id)
    : NotFoundException($"Note with id '{id}' was not found.");

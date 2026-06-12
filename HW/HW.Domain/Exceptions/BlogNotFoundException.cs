namespace HW.Domain.Exceptions;

public sealed class BlogNotFoundException(string id)
    : NotFoundException($"Blog with id '{id}' was not found.");

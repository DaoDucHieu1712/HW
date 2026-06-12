namespace HW.Domain.Exceptions;

public class VocabNotFoundException : NotFoundException
{
    public VocabNotFoundException(string id)
        : base($"Vocab with id '{id}' was not found.") { }
}

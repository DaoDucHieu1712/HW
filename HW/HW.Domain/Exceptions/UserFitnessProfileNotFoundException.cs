namespace HW.Domain.Exceptions;

public class UserFitnessProfileNotFoundException : NotFoundException
{
    public UserFitnessProfileNotFoundException(string id)
        : base($"UserFitnessProfile with id '{id}' was not found.") { }
}

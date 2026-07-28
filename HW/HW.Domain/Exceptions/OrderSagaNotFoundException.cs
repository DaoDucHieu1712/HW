namespace HW.Domain.Exceptions;

public class OrderSagaNotFoundException : NotFoundException
{
    public OrderSagaNotFoundException(string id)
        : base($"Order saga with id '{id}' was not found.") { }
}

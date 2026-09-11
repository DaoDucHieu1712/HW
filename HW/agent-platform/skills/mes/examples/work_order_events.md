// Every transition is an event with an actor, a resource and a UTC timestamp.
public sealed record OperationCompleted(
    Guid WorkOrderId,
    Guid UnitId,
    int RoutingSequence,
    string ResourceCode,
    string OperatorId,
    int QuantityGood,
    int QuantityScrapped,
    string? ScrapReasonCode,
    DateTime OccurredAtUtc) : IDomainEvent;

// Quantity is conserved at the aggregate boundary -- the rule cannot be bypassed
// by whichever slice happens to call it.
public Result Complete(int good, int scrapped, string? scrapReason, IClock clock)
{
    if (good + scrapped != InProcessQuantity)
        return Result.Failure($"Quantity does not reconcile: {good} + {scrapped} != {InProcessQuantity}.");
    if (scrapped > 0 && string.IsNullOrWhiteSpace(scrapReason))
        return Result.Failure("Scrap requires a reason code.");
    if (Sequence != Routing.NextSequenceFor(UnitId))
        return Result.Failure("Operations must complete in routing order.");

    Raise(new OperationCompleted(WorkOrderId, UnitId, Sequence, ResourceCode,
                                 OperatorId, good, scrapped, scrapReason, clock.UtcNow));
    return Result.Success();
}

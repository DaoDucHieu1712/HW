// The event and the state change commit together, or not at all.
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var events = ChangeTracker.Entries<AggregateRoot>()
        .SelectMany(e => e.Entity.DrainDomainEvents())
        .Select(e => OutboxMessage.From(e, correlationId: _context.CorrelationId))
        .ToList();

    OutboxMessages.AddRange(events);
    return await base.SaveChangesAsync(ct);      // one transaction, both writes
}

// At-least-once delivery means a repeat must be a no-op, not a second effect.
public async Task Handle(OrderPlaced message, CancellationToken ct)
{
    if (await _processed.ExistsAsync(message.MessageId, ct))
        return;

    await _reservations.ReserveAsync(message.OrderId, message.Lines, ct);
    await _processed.RecordAsync(message.MessageId, ct);
}

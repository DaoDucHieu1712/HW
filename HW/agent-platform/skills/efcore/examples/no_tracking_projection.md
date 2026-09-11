// Loads whole aggregates, tracks them, then throws most of it away.
var slow = await db.Orders
    .Include(o => o.Lines)
    .Include(o => o.Payments)     // second collection -> cartesian explosion
    .Where(o => o.CustomerId == customerId)
    .ToListAsync(ct);

// Reads only what the screen needs, untracked, one round trip.
var fast = await db.Orders
    .AsNoTracking()
    .Where(o => o.CustomerId == customerId)
    .Select(o => new OrderSummary(
        o.Id,
        o.PlacedAt,
        o.Lines.Sum(l => l.Quantity * l.UnitPrice),
        o.Lines.Count))
    .ToListAsync(ct);

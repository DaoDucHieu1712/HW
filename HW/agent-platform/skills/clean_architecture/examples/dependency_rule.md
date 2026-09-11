// Application layer owns the abstraction it needs.
namespace HW.Application.Abstractions;

public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
}

// Infrastructure implements it. The arrow points inward: Infrastructure -> Application.
namespace HW.Infrastructure.Persistence;

internal sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddAsync(Order order, CancellationToken ct) =>
        await db.Orders.AddAsync(order, ct);
}

// The host wires them together -- the only place that sees both sides.
services.AddScoped<IOrderRepository, OrderRepository>();

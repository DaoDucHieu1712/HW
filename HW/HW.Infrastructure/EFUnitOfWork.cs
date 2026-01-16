using HW.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HW.Infrastructure;

public class EFUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _dbContext;

    public EFUnitOfWork(ApplicationDbContext dbContext)
        => _dbContext = dbContext;

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync();
    }

    public async Task ExecuteAsync(Func<Task> action)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                await action();                 
                await _dbContext.SaveChangesAsync();  
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    => await _dbContext.DisposeAsync();
}

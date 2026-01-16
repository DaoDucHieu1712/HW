namespace HW.Domain.Abstractions;

//public interface IUnitOfWork : IDisposable
//{
//    Task BeginTransactionAsync();
//    Task CommitAsync();
//    Task RollbackAsync();
//    Task<int> SaveChangesAsync();
//}


public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Call save change from db context
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<Task> action);
}
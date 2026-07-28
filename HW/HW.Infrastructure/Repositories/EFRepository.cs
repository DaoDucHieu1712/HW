using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Linq.Expressions;

namespace HW.Infrastructure.Repositories;

public class EFRepository<TEntity>
        : IRepository<TEntity>
        where TEntity : Entity
{

    private readonly ApplicationDbContext _dbContext;

    public EFRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<TEntity> FindAll(Expression<Func<TEntity, bool>>? predicate = null,
        params Expression<Func<TEntity, object>>[] includeProperties)
    {
        IQueryable<TEntity> items = _dbContext.Set<TEntity>().AsNoTracking(); // Importance Always include AsNoTracking for Query Side
        if (includeProperties != null)
            foreach (var includeProperty in includeProperties)
                items = items.Include(includeProperty);

        if (predicate is not null)
            items = items.Where(predicate);

        return items;
    }

    public IQueryable<TEntity> FindAllIgnoreFilters(Expression<Func<TEntity, bool>>? predicate = null,
        params Expression<Func<TEntity, object>>[] includeProperties)
    {
        IQueryable<TEntity> items = _dbContext.Set<TEntity>().AsNoTracking().IgnoreQueryFilters();
        if (includeProperties != null)
            foreach (var includeProperty in includeProperties)
                items = items.Include(includeProperty);

        if (predicate is not null)
            items = items.Where(predicate);

        return items;
    }

    public async Task<TEntity> FindByIdAsync(string Id, CancellationToken cancellationToken = default, params Expression<Func<TEntity, object>>[] includeProperties)
        => await FindAll(null, includeProperties)
                .AsTracking()
                .SingleOrDefaultAsync(x => x.Id == Id, cancellationToken);

    public async Task<TEntity> FindSingleAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default, params Expression<Func<TEntity, object>>[] includeProperties)
        => await FindAll(null, includeProperties)
                .AsTracking()
                .SingleOrDefaultAsync(predicate, cancellationToken);

    public void Add(TEntity entity)
        => _dbContext.Add(entity);

    public void Remove(TEntity entity)
        => _dbContext.Set<TEntity>().Remove(entity);

    public void RemoveMultiple(List<TEntity> entities)
        => _dbContext.Set<TEntity>().RemoveRange(entities);

    public void Update(TEntity entity)
        => _dbContext.Set<TEntity>().Update(entity);

    public void AddRange(List<TEntity> entity)
        => _dbContext.Set<TEntity>().AddRange(entity);
}
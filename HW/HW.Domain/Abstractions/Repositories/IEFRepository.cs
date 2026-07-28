using HW.Domain.Abstractions.Entities;
using System.Linq.Expressions;

namespace HW.Domain.Abstractions.Repositories;

public interface IRepository<TEntity>
        where TEntity : Entity
{
    IQueryable<TEntity> FindAll(Expression<Func<TEntity, bool>>? predicate = null, params Expression<Func<TEntity, object>>[] includeProperties);

    IQueryable<TEntity> FindAllIgnoreFilters(Expression<Func<TEntity, bool>>? predicate = null, params Expression<Func<TEntity, object>>[] includeProperties);

    Task<TEntity> FindByIdAsync(string Id, CancellationToken cancellationToken = default, params Expression<Func<TEntity, object>>[] includeProperties);

    Task<TEntity> FindSingleAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default, params Expression<Func<TEntity, object>>[] includeProperties);

    void Add(TEntity entity);

    void AddRange(List<TEntity> entity);

    void Update(TEntity entity);

    void Remove(TEntity entity);

    void RemoveMultiple(List<TEntity> entities);
}

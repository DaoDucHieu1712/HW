using HW.Domain.Abstractions.Entities;
using HW.Infrastructure.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HW.Infrastructure.Interceptors;

public class AuditableEntitiesInterceptor : SaveChangesInterceptor
{

    private readonly UserInfo userInfo;

    public AuditableEntitiesInterceptor(UserInfo userInfo)
    {
        this.userInfo = userInfo;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        DbContext? dbContext = eventData.Context;

        if (dbContext is null)
        {
            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        IEnumerable<EntityEntry<IAuditableEntity>> entries =
            dbContext
                .ChangeTracker
                .Entries<IAuditableEntity>();

        foreach (var entityEntry in entries)
        {
            if (entityEntry.Entity is IAuditableEntity auditable)
            {
                if (entityEntry.State == EntityState.Added)
                {
                    entityEntry.Property(a => a.CreatedAt).CurrentValue = DateTimeOffset.UtcNow;
                    entityEntry.Property(a => a.CreatedBy).CurrentValue = "hddev";
                    entityEntry.Property(a => a.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
                    entityEntry.Property(a => a.UpdatedBy).CurrentValue = "hddev";
                }

                if (entityEntry.State == EntityState.Modified)
                {
                    entityEntry.Property(a => a.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
                    entityEntry.Property(a => a.UpdatedBy).CurrentValue = "hddev";

                }
            }

            if (entityEntry.Entity is ISoftDeleteEntity softDelete)
            {
                if (entityEntry.State == EntityState.Added)
                {
                    entityEntry.Property(nameof(ISoftDeleteEntity.IsDelete)).CurrentValue = false;
                }
            }
        }

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }
}

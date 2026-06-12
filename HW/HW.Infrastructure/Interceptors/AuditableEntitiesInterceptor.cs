using System.Security.Claims;
using HW.Domain.Abstractions.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HW.Infrastructure.Interceptors;

public class AuditableEntitiesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditableEntitiesInterceptor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private string CurrentUser =>
        _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "hddv";

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var user = CurrentUser;

        foreach (var entry in dbContext.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(a => a.CreatedAt).CurrentValue = now;
                entry.Property(a => a.CreatedBy).CurrentValue = user;
                entry.Property(a => a.UpdatedAt).CurrentValue = now;
                entry.Property(a => a.UpdatedBy).CurrentValue = user;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Property(a => a.UpdatedAt).CurrentValue = now;
                entry.Property(a => a.UpdatedBy).CurrentValue = user;
            }

            if (entry.Entity is ISoftDeleteEntity && entry.State == EntityState.Added)
                entry.Property(nameof(ISoftDeleteEntity.IsDelete)).CurrentValue = false;
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

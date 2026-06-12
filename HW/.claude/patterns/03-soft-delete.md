# Pattern: Soft Delete

## What
Entities implementing `ISoftDeleteEntity` are never physically removed. `Remove()` sets `IsDelete = true` then calls `Update()`. Queries must filter `IsDelete != true`.

## Interface

```csharp
public interface ISoftDeleteEntity
{
    bool? IsDelete { get; set; }  // nullable — null = not set, false = active, true = deleted
}
```

`AuditableEntitiesInterceptor` auto-sets `IsDelete = false` on Add:
```csharp
if (entry.State == EntityState.Added)
    entry.Entity.IsDelete ??= false;  // ??= means: only set if currently null
```

## Correct Remove implementation

```csharp
// ✅ Soft delete in service
public async Task Remove(string id)
{
    await _uow.ExecuteAsync(async () =>
    {
        var entity = await _blogRepository.FindByIdAsync(id);
        entity.IsDelete = true;
        _blogRepository.Update(entity);   // mark as deleted, Save is called by UoW
    });
}
```

## Query filter (exclude deleted)

`FindAll()` does NOT automatically filter soft-deleted records — you must filter explicitly:

```csharp
// Always filter on query side
var source = _repository.FindAll(x => x.IsDelete != true);

// Or in a composed query
var source = _repository
    .FindAll()
    .Where(x => x.IsDelete != true);
```

## Global query filter (optional — set in DbContext)

To filter automatically at the DbContext level, add to `OnModelCreating`:
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Auto-filter all ISoftDeleteEntity tables
    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
    {
        if (typeof(ISoftDeleteEntity).IsAssignableFrom(entityType.ClrType))
        {
            var param = Expression.Parameter(entityType.ClrType, "e");
            var prop = Expression.Property(param, nameof(ISoftDeleteEntity.IsDelete));
            var notTrue = Expression.NotEqual(prop, Expression.Constant(true, typeof(bool?)));
            var lambda = Expression.Lambda(notTrue, param);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}
```
> Note: if global filter is added, remove manual `.Where(x => x.IsDelete != true)` from services to avoid double filtering.

## When to use vs hard delete

| Scenario | Use |
|---|---|
| User-created content (blogs, products) | Soft delete — audit trail required |
| Lookup/master data | Soft delete |
| Temporary join/pivot tables | Hard delete (`_repo.Remove()`) is acceptable |
| Identity tables (users, roles) | Use Identity API (`LockoutEnabled`, not delete) |

## What NOT to do

```csharp
// ❌ Hard delete on auditable entities
_blogRepository.Remove(entity);

// ❌ Forget to filter deleted records in queries
var all = await _repository.FindAll().ToListAsync();  // includes deleted!

// ❌ Set IsDelete in constructor — interceptor handles it
public Blog() { IsDelete = false; }  // redundant, but harmless

// ✅ Always filter:
var all = await _repository.FindAll(x => x.IsDelete != true).ToListAsync();
```

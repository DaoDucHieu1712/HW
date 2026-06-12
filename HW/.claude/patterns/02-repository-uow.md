# Pattern: Repository + Unit of Work

## What
`IEFRepository<T>` abstracts data access. `IUnitOfWork.ExecuteAsync()` wraps every mutation in a single `SaveChanges` call. No direct DbContext usage outside Infrastructure.

## Core contract

```csharp
// Query side — always AsNoTracking (read-only)
IQueryable<TEntity> FindAll(predicate?, includeProperties[])

// Single-item fetch — AsTracking (needed before mutation)
Task<TEntity> FindByIdAsync(string id, ct, includeProperties[])
Task<TEntity> FindSingleAsync(predicate?, ct, includeProperties[])

// Commands — called inside UoW.ExecuteAsync(), never SaveChanges manually
void Add(TEntity)
void AddRange(List<TEntity>)
void Update(TEntity)
void Remove(TEntity)             // hard delete — don't use on auditable entities
void RemoveMultiple(List<TEntity>)

// UoW
Task ExecuteAsync(Func<Task> action)   // calls action(), then SaveChangesAsync()
Task SaveChangesAsync(CancellationToken)
```

## Query pattern

```csharp
// FindAll returns IQueryable — compose before materialising
public async Task<PagedResult<BlogResponseDto>> FindAllAndPaging(BlogPagingAndFilterRequestDto dto)
{
    var source = _blogRepository.FindAll();  // IQueryable, no DB hit yet

    if (!string.IsNullOrWhiteSpace(dto.Search))
        source = source.Where(b =>
            b.Title!.ToUpper().Contains(dto.Search.ToUpper()) ||
            b.Content!.ToUpper().Contains(dto.Search.ToUpper()));

    // PagedResult.CreateAsync hits the DB twice: COUNT + paginated SELECT
    var paged = await PagedResult<Blog>.CreateAsync(source, dto.PageIndex, dto.PageSize);
    return paged.Adapt<PagedResult<BlogResponseDto>>();
}
```

## Mutation pattern (all mutations)

```csharp
// Insert
public async Task Insert(CreateBlogRequestDto dto)
{
    await _uow.ExecuteAsync(async () =>
    {
        var entity = new Blog(dto.Title, dto.Content);
        _blogRepository.Add(entity);
        // SaveChangesAsync is called automatically by ExecuteAsync
    });
}

// Update (partial)
public async Task Update(UpdateBlogRequestDto dto)
{
    await _uow.ExecuteAsync(async () =>
    {
        var entity = await _blogRepository.FindByIdAsync(dto.Id);
        if (dto.Title != null) entity.Title = dto.Title;
        if (dto.Content != null) entity.Content = dto.Content;
        _blogRepository.Update(entity);
    });
}

// Batch insert
public async Task AddRange(List<CreateBlogRequestDto> dtos)
{
    await _uow.ExecuteAsync(async () =>
    {
        _blogRepository.AddRange(dtos.Adapt<List<Blog>>());
    });
}
```

## FindAll vs FindByIdAsync — tracking rule

| Method | Tracking | Use for |
|--------|----------|---------|
| `FindAll()` | `AsNoTracking` | Read-only queries, lists, projections |
| `FindByIdAsync()` | `AsTracking` | Fetching before Update/Remove |
| `FindSingleAsync()` | `AsTracking` | Fetching by predicate before mutation |

```csharp
// EFRepository internals (explains the rule):
public IQueryable<TEntity> FindAll(...) =>
    _dbContext.Set<TEntity>().AsNoTracking()...;   // ← always no tracking

public async Task<TEntity> FindByIdAsync(string Id, ...) =>
    await FindAll(...)
        .AsTracking()                              // ← re-adds tracking for mutations
        .SingleOrDefaultAsync(x => x.Id == Id);
```

## What NOT to do

```csharp
// ❌ Don't inject DbContext into services
public class BlogService(ApplicationDbContext db) { ... }

// ❌ Don't call SaveChangesAsync manually
_blogRepository.Add(entity);
await _dbContext.SaveChangesAsync();   // wrong — use UoW

// ❌ Don't use FindAll() result for mutation (no tracking)
var entity = _blogRepository.FindAll(x => x.Id == id).First();
entity.Title = "new";
_blogRepository.Update(entity);   // EF may not detect changes

// ✅ Always fetch with FindByIdAsync before mutating
var entity = await _blogRepository.FindByIdAsync(id);
entity.Title = "new";
_blogRepository.Update(entity);
```

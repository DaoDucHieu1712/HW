# Pattern: Pagination

## What
`PagedResult<T>` wraps list responses. All list endpoints accept `PageIndex` + `PageSize` + optional `Search`. `PagedResult.CreateAsync()` handles the COUNT and paginated SELECT in two efficient queries.

## PagedResult contract

```csharp
class PagedResult<T>
{
    List<T> Items          // current page items
    int PageIndex          // 1-based current page
    int PageSize           // items per page
    int TotalCount         // total matching records
    int TotalPages         // ceil(TotalCount / PageSize)
    bool HasNextPage       // PageIndex * PageSize < TotalCount
    bool HasPreviousPage   // PageIndex > 1

    // Limits: DefaultPageSize=10, UpperPageSize=100, DefaultPageIndex=1
}
```

## Standard paging request DTO

```csharp
public record {Feature}PagingRequestDto(
    string? Search,
    int PageIndex,
    int PageSize);
```

## Service implementation

```csharp
public async Task<PagedResult<BlogResponseDto>> FindAllAndPaging(BlogPagingAndFilterResponseDto dto)
{
    var source = _blogRepository.FindAll();   // IQueryable — no DB hit yet

    // 1. Filter (all filters before paging)
    if (!string.IsNullOrWhiteSpace(dto.Search))
        source = source.Where(b =>
            b.Title!.ToUpper().Contains(dto.Search.ToUpper()) ||
            b.Content!.ToUpper().Contains(dto.Search.ToUpper()));

    // 2. Always exclude soft-deleted
    source = source.Where(x => x.IsDelete != true);

    // 3. Sort (always sort before paging for stable results)
    source = source.OrderByDescending(x => x.CreatedAt);

    // 4. Paginate + materialise
    var paged = await PagedResult<Blog>.CreateAsync(source, dto.PageIndex, dto.PageSize);

    // 5. Map entities → DTOs
    return paged.Adapt<PagedResult<BlogResponseDto>>();
}
```

## Controller binding

```csharp
// [FromQuery] binds the paging DTO from query string params:
// GET /api/blog?search=hello&pageIndex=2&pageSize=20
[HttpGet]
public async Task<IActionResult> GetAll([FromQuery] BlogPagingAndFilterResponseDto dto)
{
    var result = await _blogService.FindAllAndPaging(dto);
    return Ok(ApiResponseFactory.Success(result));
}
```

## Response shape (JSON)

```json
{
  "success": true,
  "statusCode": 200,
  "message": "Operation successful",
  "data": {
    "items": [ { "id": "...", "title": "..." } ],
    "pageIndex": 2,
    "pageSize": 10,
    "totalCount": 47,
    "totalPages": 5,
    "hasNextPage": true,
    "hasPreviousPage": true
  }
}
```

## PagedResult.CreateAsync internals

```csharp
public static async Task<PagedResult<T>> CreateAsync(IQueryable<T> query, int pageIndex, int pageSize)
{
    // Clamp inputs
    pageIndex = pageIndex <= 0 ? DefaultPageIndex : pageIndex;
    pageSize  = pageSize  <= 0 ? DefaultPageSize  : pageSize > UpperPageSize ? UpperPageSize : pageSize;

    // Two DB queries:
    var totalCount = await query.CountAsync();                             // SELECT COUNT(*)
    var items      = await query.Skip((pageIndex - 1) * pageSize)         // SELECT ... LIMIT/OFFSET
                                .Take(pageSize)
                                .ToListAsync();

    return new(items, pageIndex, pageSize, totalCount);
}
```

## Mapster PagedResult mapping

Mapster doesn't auto-map `PagedResult<Entity>` → `PagedResult<Dto>` without config. Register in `MappingConfig`:

```csharp
config.NewConfig<PagedResult<Blog>, PagedResult<BlogResponseDto>>()
    .Map(dest => dest.Items, src => src.Items.Adapt<List<BlogResponseDto>>());
```

## What NOT to do

```csharp
// ❌ Materialise before filtering (loads all rows into memory)
var all = await _repo.FindAll().ToListAsync();
var filtered = all.Where(x => x.Title.Contains(search)).ToList();

// ❌ Skip paging on large datasets
var all = await _repo.FindAll().ToListAsync();   // unbounded result

// ❌ Sort after paging (wrong order)
var items = source.Skip(skip).Take(take).ToList();
var sorted = items.OrderBy(x => x.Name);   // sorts current page only

// ✅ Filter → Sort → PagedResult.CreateAsync → Adapt
```

# Guideline: Coding Style

## Naming

| Element | Convention | Example |
|---------|-----------|---------|
| Classes, interfaces, records | PascalCase | `BlogService`, `IBlogService`, `BlogResponseDto` |
| Methods | PascalCase | `FindAllAndPaging`, `Insert` |
| Private fields | `_camelCase` | `_blogRepository`, `_uow` |
| Parameters / locals | `camelCase` | `blogDto`, `pageIndex` |
| Constants | PascalCase | `DefaultPageSize`, `UpperPageSize` |
| Namespaces | Match folder path | `HW.Application.Services` |
| Generic type params | `T`, `TEntity`, `TResult` | |

## File naming

- One class/interface per file (unless service + interface in same file by convention)
- Exception: Service + Interface share a file: `BlogService.cs` contains both `IBlogService` and `BlogService`
- Exception: All DTOs for a feature in one file: `BlogDtos.cs`
- Exception: Validators for a feature in one file: `BlogValidator.cs` as static class with nested validators

## Using directives

- Always declare explicit `using` directives — don't rely on `global using` for domain types
- `using static HW.Application.Dtos.BlogDtos;` pattern for DTO access in controllers
- Order: System → Microsoft → Third-party → Project (alphabetical within each group)

## Null handling

```csharp
// Use nullable reference types — project has <Nullable>enable</Nullable>
string? Title     // nullable — can be null
string Title      // non-nullable — must have value

// Prefer ?? for defaults
var name = dto.Name ?? string.Empty;

// Prefer ?. for safe access
var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

// Use ??= for lazy init
entity.IsDelete ??= false;   // only set if currently null
```

## Async conventions

```csharp
// All service methods are async Task
public async Task Insert(CreateBlogRequestDto dto) { ... }
public async Task<BlogResponseDto> FindById(string id) { ... }
public async Task<PagedResult<BlogResponseDto>> FindAllAndPaging(...) { ... }

// Suffix private async helpers with Async
private async Task<string> GenerateJwtTokenAsync(...) { ... }

// Never use .Result or .Wait() — always await
var user = await _userManager.FindByNameAsync(dto.Username);   // ✅
var user = _userManager.FindByNameAsync(dto.Username).Result;  // ❌
```

## Constructor injection

```csharp
// Prefer primary constructor style (C# 12) for simple cases
public class BlogService(
    IEFRepository<Blog> blogRepository,
    IUnitOfWork uow)
{
    private readonly IEFRepository<Blog> _blogRepository = blogRepository;
    private readonly IUnitOfWork _uow = uow;
}

// Or traditional — current project uses this:
public class BlogService : IBlogService
{
    private readonly IEFRepository<Blog> _blogRepository;
    private readonly IUnitOfWork _uow;

    public BlogService(IEFRepository<Blog> blogRepository, IUnitOfWork uow)
    {
        _blogRepository = blogRepository;
        _uow = uow;
    }
}
```

## String handling

```csharp
// Use string.Empty for non-nullable defaults (not "")
public string Title { get; set; } = string.Empty;

// Use IsNullOrWhiteSpace for guard checks
if (string.IsNullOrWhiteSpace(dto.Username))
    throw new ArgumentException("Username is required");

// Use ToUpper() for case-insensitive search (current project pattern)
source = source.Where(b => b.Title!.ToUpper().Contains(search.ToUpper()));
// Better alternative: use EF Core StringComparison or SQL COLLATE
```

## Record 'with' expressions

```csharp
// Immutable records — use 'with' to create modified copies
dto = dto with { Id = id };   // sets Id without mutating original

// Preferred in controller to bind route param into DTO
[HttpPut("{id}")]
public async Task<IActionResult> Update(string id, [FromBody] UpdateBlogRequestDto dto)
{
    dto = dto with { Id = id };
    await _service.Update(dto);
    return NoContent();
}
```

## Expression bodies

```csharp
// Single-expression methods → use expression body
public void Add(TEntity entity) => _dbContext.Add(entity);

public EFRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

// Multi-statement methods → always use block body
public async Task Insert(CreateBlogRequestDto dto)
{
    await _uow.ExecuteAsync(async () =>
    {
        var entity = new Blog(dto.Title, dto.Content);
        _blogRepository.Add(entity);
    });
}
```

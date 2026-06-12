# Pattern: DTOs as Records

## What
All DTOs in this project are C# `record` types inside a static class grouped by feature. Records give value-equality, immutability, and concise syntax.

## Actual structure (BlogDtos.cs)

```csharp
namespace HW.Application.Dtos;

public static class BlogDtos
{
    // Response DTO — full data including audit fields
    public record BlogResponseDto(
        string Id,
        string? Title,
        string? Content,
        DateTimeOffset? CreatedAt,
        string? CreatedBy,
        DateTimeOffset? UpdatedAt,
        string? UpdatedBy);

    // Paging/filter request — from query string
    public record BlogPagingAndFilterResponseDto(
        string? Search,
        int PageIndex,
        int PageSize);

    // Create request — only creation fields
    public record CreateBlogRequestDto(
        string? Title,
        string? Content);

    // Update request — Id + nullable fields (partial update)
    public record UpdateBlogRequestDto(
        string Id,
        string? Title,
        string? Content);
}
```

Controller imports via `static`:
```csharp
using static HW.Application.Dtos.BlogDtos;
// Then use directly: CreateBlogRequestDto, BlogResponseDto, etc.
```

## Template for a new feature

```csharp
namespace HW.Application.Dtos;

public static class {Feature}Dtos
{
    public record {Feature}ResponseDto(
        string Id,
        // ... domain properties
        DateTimeOffset? CreatedAt,
        string? CreatedBy,
        DateTimeOffset? UpdatedAt,
        string? UpdatedBy);

    public record {Feature}PagingRequestDto(
        string? Search,
        int PageIndex = 1,
        int PageSize = 10);

    public record Create{Feature}RequestDto(
        // required creation fields
    );

    public record Update{Feature}RequestDto(
        string Id,
        // nullable fields — only those that can be updated
    );
}
```

## Record vs class — when to use each

| Use `record` | Use `class` |
|---|---|
| DTOs (immutable data shapes) | Entities (mutable, tracked by EF) |
| Request / Response contracts | Services, Repositories |
| Value objects with no behavior | Complex objects with methods |

## Positional vs property records

```csharp
// Positional — concise, constructor injection
public record CreateBlogRequestDto(string? Title, string? Content);

// Property-based — use when defaults or mutability needed
public record BlogPagingRequestDto
{
    public string? Search { get; init; }
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}
```

> Use property-based records for paging DTOs that need default values, since positional records can't have defaults when bound from query strings without a special model binder.

## Mapster with records

Records work with Mapster's `.Adapt<T>()` automatically since they are compiled to classes:
```csharp
// Entity → Response record
var dto = entity.Adapt<BlogResponseDto>();

// Request record → Entity
var entity = dto.Adapt<Blog>();

// List<Entity> → List<record>
var dtos = entities.Adapt<List<BlogResponseDto>>();

// PagedResult<Entity> → PagedResult<record>
var pagedDtos = paged.Adapt<PagedResult<BlogResponseDto>>();
```

## IMappingRegister

Use `IMappingRegister` (not Mapster's `IRegister`) to define configs:
```csharp
// HW.Application/Common/Mapping/{Feature}MappingConfig.cs
using Mapster;
using HW.Application.Common.Mapping;

namespace HW.Application.Common.Mapping;

public class {Feature}MappingConfig : IMappingRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<{Feature}, {Feature}ResponseDto>();

        config.NewConfig<Create{Feature}RequestDto, {Feature}>();

        config.NewConfig<Update{Feature}RequestDto, {Feature}>()
            .IgnoreNullValues(true);  // partial update support
    }
}
```

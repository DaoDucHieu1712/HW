# Pattern: Partial Update

## What
`Update` request DTOs use nullable fields. The service only overwrites properties that are non-null in the request, preserving existing values for omitted fields. No PATCH endpoint needed.

## DTO shape

```csharp
// Id required, everything else nullable = optional to update
public record UpdateBlogRequestDto(
    string Id,       // required — identifies the record
    string? Title,   // null = "don't change", value = "update to this"
    string? Content);
```

## Service implementation

```csharp
public async Task Update(UpdateBlogRequestDto dto)
{
    await _uow.ExecuteAsync(async () =>
    {
        // Must use FindByIdAsync (tracked) before mutation
        var entity = await _blogRepository.FindByIdAsync(dto.Id);

        // Only apply non-null fields
        if (dto.Title != null)   entity.Title   = dto.Title;
        if (dto.Content != null) entity.Content = dto.Content;

        _blogRepository.Update(entity);
    });
}
```

## For value types (int, decimal, bool)

Value types can't be null. Use nullable wrapper `int?`, `decimal?`, `bool?`:

```csharp
public record UpdateProductRequestDto(
    string Id,
    string? Name,        // nullable reference type
    string? Description,
    decimal? Price,      // nullable value type
    int? Stock,
    bool? IsActive);

// Service
if (dto.Price.HasValue)    entity.Price    = dto.Price.Value;
if (dto.Stock.HasValue)    entity.Stock    = dto.Stock.Value;
if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;
```

## Validator for update DTOs

Only validate fields when they are provided (`.When()` guard):

```csharp
public class UpdateProductRequestDtoValidator : AbstractValidator<UpdateProductRequestDto>
{
    public UpdateProductRequestDtoValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        // Optional fields: only validate when provided
        RuleFor(x => x.Name)
            .MinimumLength(3).MaximumLength(100)
            .When(x => x.Name != null);

        RuleFor(x => x.Price)
            .GreaterThan(0)
            .When(x => x.Price.HasValue);

        RuleFor(x => x.Stock)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Stock.HasValue);
    }
}
```

## Mapster alternative (IgnoreNullValues)

For entities with many fields, use Mapster's `IgnoreNullValues` instead of manual null checks:

```csharp
// MappingConfig
config.NewConfig<UpdateProductRequestDto, Product>()
    .IgnoreNullValues(true);   // skips null source values

// Service
public async Task Update(UpdateProductRequestDto dto)
{
    await _uow.ExecuteAsync(async () =>
    {
        var entity = await _repo.FindByIdAsync(dto.Id);
        dto.Adapt(entity);   // merges non-null values onto the tracked entity
        _repo.Update(entity);
    });
}
```

> Prefer manual null checks for clarity on small DTOs. Use `IgnoreNullValues` when there are 6+ updatable fields.

## Controller — binding Id from route

```csharp
[HttpPut("{id}")]
[Authorize]
public async Task<IActionResult> Update(string id, [FromBody] UpdateBlogRequestDto dto)
{
    // Records are immutable: use 'with' expression to override Id
    dto = dto with { Id = id };
    await _blogService.Update(dto);
    return NoContent();
}
```

## What NOT to do

```csharp
// ❌ Overwrite all fields unconditionally
entity.Title = dto.Title;     // sets Title to null if not provided
entity.Content = dto.Content;

// ❌ Separate PATCH endpoint for "partial" — redundant
[HttpPatch("{id}")]
public async Task<IActionResult> Patch(string id, JsonPatchDocument patch) { ... }

// ✅ One PUT endpoint with nullable DTO fields
if (dto.Title != null) entity.Title = dto.Title;
```

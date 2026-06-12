# Pattern: API Response Wrapping

## What
Every controller action returns `ApiResponse<T>` via `ApiResponseFactory`. Clients always get a consistent envelope regardless of success or failure. Errors go through `ExceptionHandlingMiddleware` — controllers never manually build error responses.

## ApiResponseFactory — full method reference

```csharp
// Success with data (GET, POST returning resource)
ApiResponseFactory.Success<T>(T data, string message = "Operation successful", int statusCode = 200)

// Success without data (POST/PUT/DELETE returning 204)
ApiResponseFactory.Success(string message, int statusCode)

// Created (POST that returns the new resource)
ApiResponseFactory.Created<T>(T data, string message = "Resource created successfully")

// Error (generic bad request)
ApiResponseFactory.Error<T>(string message, int statusCode = 400, IEnumerable<string>? errors = null)

// Validation failure — 422
ApiResponseFactory.ValidationError<T>(IEnumerable<string> errors, string message = "Validation failed")

// Auth failures
ApiResponseFactory.Unauthorized<T>(string message)   // 401
ApiResponseFactory.Forbidden<T>(string message)      // 403

// Not found — 404
ApiResponseFactory.NotFound<T>(string message)

// Server error — 500
ApiResponseFactory.ServerError<T>(string message, IEnumerable<string>? errors = null)
```

## Controller patterns

```csharp
// GET list — always return data
[HttpGet]
public async Task<IActionResult> GetAll([FromQuery] BlogPagingAndFilterResponseDto dto)
{
    var result = await _blogService.FindAllAndPaging(dto);
    return Ok(ApiResponseFactory.Success(result));
}

// GET single
[HttpGet("{id}")]
public async Task<IActionResult> GetById(string id)
{
    var result = await _blogService.FindById(id);
    return Ok(ApiResponseFactory.Success(result));
}

// POST create — return 204 NoContent (current pattern in this project)
[HttpPost]
[Authorize]
public async Task<IActionResult> Create([FromBody] CreateBlogRequestDto dto)
{
    await _blogService.Insert(dto);
    return NoContent();   // 204
}

// POST create — return 201 Created (alternative, preferred for REST)
[HttpPost]
[Authorize]
public async Task<IActionResult> Create([FromBody] CreateProductRequestDto dto)
{
    var id = await _productService.Insert(dto);
    return StatusCode(201, ApiResponseFactory.Created(new { id }));
}

// PUT update — 204 NoContent
[HttpPut("{id}")]
[Authorize]
public async Task<IActionResult> Update(string id, [FromBody] UpdateBlogRequestDto dto)
{
    dto = dto with { Id = id };   // records: use 'with' expression to set Id
    await _blogService.Update(dto);
    return NoContent();
}

// DELETE — 204 NoContent
[HttpDelete("{id}")]
[Authorize]
public async Task<IActionResult> Delete(string id)
{
    await _blogService.Remove(id);
    return NoContent();
}
```

## What the envelope looks like (JSON)

```json
// Success with data
{
  "success": true,
  "statusCode": 200,
  "message": "Operation successful",
  "errors": null,
  "data": { "id": "...", "title": "...", "content": "..." },
  "timestamp": "2026-03-25T10:00:00Z"
}

// Validation failure (422)
{
  "success": false,
  "statusCode": 422,
  "message": "One or more validation errors occurred",
  "errors": ["Title: Title is required.", "Content: Content must not exceed 200 characters."],
  "data": null,
  "timestamp": "2026-03-25T10:00:00Z"
}

// Domain error (400)
{
  "success": false,
  "statusCode": 400,
  "message": "A blog with this title already exists.",
  "errors": ["Bad Request"],
  "data": null,
  "timestamp": "2026-03-25T10:00:00Z"
}
```

## Middleware vs controller error handling

| Error type | Handler | Why |
|---|---|---|
| Domain/business errors | `ExceptionHandlingMiddleware` | Throw in service, auto-mapped |
| Validation errors | `FluentValidationMiddleware` (pre-controller) | Stops request before action runs |
| Auth errors | ASP.NET Core auth pipeline | Returns 401/403 before controller |
| Controller logic errors | `ExceptionHandlingMiddleware` | Catches anything that bubbles |

**Rule:** Controllers must not contain try/catch. All error handling is cross-cutting.

```csharp
// ❌ Don't catch in controllers
[HttpGet("{id}")]
public async Task<IActionResult> GetById(string id)
{
    try
    {
        var result = await _service.FindById(id);
        return Ok(ApiResponseFactory.Success(result));
    }
    catch (Exception ex)   // wrong — let middleware handle
    {
        return BadRequest(ex.Message);
    }
}

// ✅ Controllers are thin — just call and wrap
[HttpGet("{id}")]
public async Task<IActionResult> GetById(string id)
{
    var result = await _service.FindById(id);
    return Ok(ApiResponseFactory.Success(result));
}
```

# Guideline: API Design

## Route conventions

```
GET    /api/{resource}          → list with paging
GET    /api/{resource}/{id}     → single by ID
POST   /api/{resource}          → create
PUT    /api/{resource}/{id}     → update (partial — nullable fields)
DELETE /api/{resource}/{id}     → soft delete
POST   /api/{resource}/batch    → bulk create (e.g. add-range)
```

Rules:
- Route segments: **lowercase**, **plural nouns** — `api/blog`, `api/product`
- No verbs in routes — `api/blog` not `api/get-blogs`
- IDs always in path — `api/blog/123` not `api/blog?id=123`

## HTTP method semantics

| Method | Idempotent | Body | Use for |
|--------|-----------|------|---------|
| GET | Yes | No | Read only — never mutates |
| POST | No | Yes | Create, bulk operations |
| PUT | Yes | Yes | Update (full or partial) |
| DELETE | Yes | No | Remove (soft in this project) |

## Response status codes

| Scenario | Code | Response body |
|----------|------|--------------|
| Successful read (GET) | 200 | `ApiResponse<T>` with data |
| Created resource | 201 | `ApiResponse<T>` with new resource (or 204) |
| Mutation succeeded (POST/PUT/DELETE) | 204 | Empty |
| Validation failed | 422 | `ApiResponse` with `errors[]` |
| Business rule violated | 400 | `ApiResponse` with message |
| Unauthenticated | 401 | `ApiResponse` with message |
| Forbidden | 403 | `ApiResponse` with message |
| Not found | 404 | `ApiResponse` with message |
| Server error | 500 | `ApiResponse` with message |

## Authentication rules

```
Public (no token needed):
  GET  /api/blog          ← reads are public
  GET  /api/blog/{id}
  POST /api/auth/register
  POST /api/auth/login

Protected ([Authorize]):
  POST   /api/blog        ← create
  PUT    /api/blog/{id}   ← update
  DELETE /api/blog/{id}   ← delete

Role-protected ([Authorize(Roles = "Admin")]):
  DELETE /api/user/{id}   ← admin-only
```

## Controller structure

```csharp
[Route("api/[controller]")]   // or explicit: [Route("api/blog")]
[ApiController]
public class BlogController : ControllerBase
{
    // 1. Private fields
    private readonly IBlogService _blogService;

    // 2. Constructor
    public BlogController(IBlogService blogService) => _blogService = blogService;

    // 3. Actions — in CRUD order: GET-list, GET-single, POST, PUT, DELETE

    [HttpGet]                                     // GET /api/blog
    public async Task<IActionResult> GetAll([FromQuery] BlogPagingAndFilterResponseDto dto)

    [HttpGet("{id}")]                             // GET /api/blog/{id}
    public async Task<IActionResult> GetById(string id)

    [HttpPost]                                    // POST /api/blog
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateBlogRequestDto dto)

    [HttpPut("{id}")]                             // PUT /api/blog/{id}
    [Authorize]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateBlogRequestDto dto)

    [HttpDelete("{id}")]                          // DELETE /api/blog/{id}
    [Authorize]
    public async Task<IActionResult> Delete(string id)
}
```

## Query string binding

```csharp
// Paging + filter from query string
[HttpGet]
public async Task<IActionResult> GetAll([FromQuery] BlogPagingAndFilterResponseDto dto)
// GET /api/blog?search=hello&pageIndex=1&pageSize=20

// Simple scalar from query string
[HttpGet("search")]
public async Task<IActionResult> Search([FromQuery] string? keyword, [FromQuery] int page = 1)
```

## Versioning (future guideline)

When adding v2 endpoints, use URL versioning:
```
/api/v1/blog
/api/v2/blog
```

Add `Asp.Versioning.Mvc` package and configure in `Program.cs`:
```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});
```

## Swagger documentation

Swagger is available at `/swagger` in Development. JWT Bearer auth is pre-configured via `AddSwaggerDocumentation()`:
- Click "Authorize" button in Swagger UI
- Enter `Bearer <your-token>`
- All subsequent requests include the Authorization header

## What NOT to do

```csharp
// ❌ Verbs in routes
[Route("api/get-all-blogs")]
[Route("api/blog/create")]

// ❌ GET with body
[HttpGet]
public IActionResult GetAll([FromBody] SearchDto dto)

// ❌ Return raw objects — always use ApiResponseFactory
return Ok(blogs);                        // bypasses envelope
return Ok(new { data = blogs });         // inconsistent shape

// ❌ Catch exceptions in controllers
try { ... } catch (Exception ex) { return BadRequest(ex.Message); }

// ✅ Always wrap, always throw
return Ok(ApiResponseFactory.Success(result));
throw new BadRequestException("...");   // middleware handles it
```

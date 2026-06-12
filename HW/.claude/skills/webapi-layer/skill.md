# Skill: webapi-layer

Controllers, ApiResponseFactory, middleware, and Swagger setup.

---

## ApiResponse model — `HW.Api/Models/ApiResponse.cs`
```csharp
namespace HW.Api.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
    public T? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public static class ApiResponseFactory
{
    public static ApiResponse<T> Success<T>(T data, string message = "Success", int statusCode = 200)
        => new() { Success = true, StatusCode = statusCode, Message = message, Data = data };

    public static ApiResponse<object> Success(string message = "Success", int statusCode = 200)
        => new() { Success = true, StatusCode = statusCode, Message = message };

    public static ApiResponse<object> Failure(string message, int statusCode = 400, List<string>? errors = null)
        => new() { Success = false, StatusCode = statusCode, Message = message, Errors = errors };
}
```

---

## Controller template — `HW.Api/Controllers/{Feature}Controller.cs`

Controllers use `ISender` (MediatR) — **not** a service interface.

```csharp
using HW.Api.Models;
using HW.Application.Features.{Feature}s.Commands.Create{Feature};
using HW.Application.Features.{Feature}s.Commands.Delete{Feature};
using HW.Application.Features.{Feature}s.Commands.Update{Feature};
using HW.Application.Features.{Feature}s.Queries.Get{Feature}ById;
using HW.Application.Features.{Feature}s.Queries.Get{Feature}s;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.{Feature}s.Dtos.{Feature}Dtos;

namespace HW.Api.Controllers;

[Route("api/{feature}")]
[ApiController]
public class {Feature}Controller : ControllerBase
{
    private readonly ISender _sender;

    public {Feature}Controller(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] {Feature}PagingRequestDto request)
    {
        var result = await _sender.Send(new Get{Feature}sQuery(request.Search, request.PageIndex, request.PageSize));
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var result = await _sender.Send(new Get{Feature}ByIdQuery(id));
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] Create{Feature}RequestDto dto)
    {
        await _sender.Send(new Create{Feature}Command(dto.Prop1, dto.Prop2));
        return NoContent();
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> Update(string id, [FromBody] Update{Feature}RequestDto dto)
    {
        await _sender.Send(new Update{Feature}Command(id, dto.Prop1, dto.Prop2));
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(string id)
    {
        await _sender.Send(new Delete{Feature}Command(id));
        return NoContent();
    }
}
```

---

## ExceptionHandlingMiddleware — `HW.Api/Middlewares/ExceptionHandlingMiddleware.cs`
```csharp
using FluentValidation;
using HW.Api.Models;
using HW.Domain.Exceptions;
using System.Text.Json;

namespace HW.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message, errors) = exception switch
        {
            NotFoundException e       => (404, e.Message, (List<string>?)null),
            BadRequestException e     => (400, e.Message, null),
            DomainException e         => (400, e.Message, null),
            ValidationException e     => (422, "Validation failed", e.Errors.Select(x => x.ErrorMessage).ToList()),
            ArgumentException e       => (400, e.Message, null),
            UnauthorizedAccessException e => (401, e.Message, null),
            _ => (500, "An unexpected error occurred.", null)
        };

        context.Response.StatusCode = statusCode;
        var response = ApiResponseFactory.Failure(message, statusCode, errors);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
```

---

## Swagger DI — `HW.Api/DI/SwaggerExtension.cs`
```csharp
using Microsoft.OpenApi.Models;

namespace HW.Api.DI;

public static class SwaggerExtension
{
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "HW API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header. Example: 'Bearer {token}'",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                }
            });
        });
        return services;
    }
}
```

---

## Program.cs middleware pipeline (current)
```csharp
// Validator registration — discovers all AbstractValidator<T> in Application assembly
builder.Services.AddValidatorsFromAssembly(HW.Application.AssemblyReference.Assembly);

// ...other services...

app.UseCors(...);
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ExceptionHandlingMiddleware>();  // catches exceptions from MediatR pipeline
app.MapControllers();
```

**Note:** `FluentValidationMiddleware` is NOT in the pipeline. Validation is handled by `ValidationBehavior` inside the MediatR pipeline, triggered when `ISender.Send()` is called.

---

## Route naming rules
- Route: `[Route("api/{feature}")]` — all lowercase, singular (e.g. `api/blog`, `api/product`)
- Sub-resources: `[HttpGet("{id}/items")]`
- Bulk operations: `[HttpPost("batch")]`

## Controller rules
- Always inject `ISender _sender` — never a service interface.
- GET endpoints return `Ok(ApiResponseFactory.Success(result))`.
- Mutations (POST/PUT/DELETE) return `NoContent()` on success.
- `[Authorize]` on mutation endpoints by default.
- Pass route `{id}` directly into the command/query record constructor.

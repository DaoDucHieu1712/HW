using FluentValidation;
using HW.Api.Models;
using System.Text.Json;

namespace HW.Api.Middlewares;

public class FluentValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FluentValidationMiddleware> _logger;

    public FluentValidationMiddleware(RequestDelegate next, ILogger<FluentValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to POST/PUT/PATCH
        if (context.Request.Method is not ("POST" or "PUT" or "PATCH"))
        {
            await _next(context);
            return;
        }

        // No body
        if (context.Request.ContentLength is null or 0)
        {
            await _next(context);
            return;
        }

        // Enable buffering to read body multiple times
        context.Request.EnableBuffering();

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
        {
            await _next(context);
            return;
        }

        // Get endpoint to determine parameter type
        var endpoint = context.GetEndpoint();
        var actionDescriptor = endpoint?
            .Metadata
            .GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>();

        var parameterType = actionDescriptor?
            .Parameters
            .FirstOrDefault(p => p.ParameterType != typeof(CancellationToken))?
            .ParameterType;

        if (parameterType == null)
        {
            await _next(context);
            return;
        }

        // Deserialize body → DTO
        var dto = JsonSerializer.Deserialize(body, parameterType, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (dto == null)
        {
            await _next(context);
            return;
        }

        // Resolve IValidator<T>
        var validatorType = typeof(IValidator<>).MakeGenericType(parameterType);
        var validator = context.RequestServices.GetService(validatorType) as IValidator;

        if (validator != null)
        {
            var validationContext = new ValidationContext<object>(dto);
            var result = await validator.ValidateAsync(validationContext);

            if (!result.IsValid)
            {
                _logger.LogWarning("Validation failed for {Type}", parameterType.Name);

                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                context.Response.ContentType = "application/json";

                var errors = result.Errors
                    .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
                    .ToList();

                var response = ApiResponseFactory.ValidationError(errors, "One or more validation errors occurred");

                var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await context.Response.WriteAsync(json);
                return; // Stop pipeline
            }
        }

        await _next(context);
    }
}

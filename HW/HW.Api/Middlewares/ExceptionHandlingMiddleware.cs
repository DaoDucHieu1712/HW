using HW.Api.Models;
using HW.Domain.Exceptions;
using System.Net;
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
            _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";

        ApiResponse<object> response;

        // Handle domain exceptions
        if (ex is DomainException domainEx)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            response = ApiResponseFactory.Error<object>(
                message: domainEx.Message,
                statusCode: 400,
                errors: new[] { domainEx.Title }
            );
        }
        // Handle validation exceptions
        else if (ex is FluentValidation.ValidationException validationEx)
        {
            context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
            var errors = validationEx.Errors
                .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
                .ToList();

            response = ApiResponseFactory.ValidationError<object>(errors, "One or more validation errors occurred");
        }
        // Handle argument exceptions
        else if (ex is ArgumentException argEx)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            response = ApiResponseFactory.Error<object>(argEx.Message, 400);
        }
        // Handle unauthorized access
        else if (ex is UnauthorizedAccessException unAuthEx)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            response = ApiResponseFactory.Unauthorized<object>(unAuthEx.Message);
        }
        // Default to server error
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            response = ApiResponseFactory.ServerError<object>(
                message: "An internal server error occurred",
                errors: new[] { ex.Message }
            );
        }

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

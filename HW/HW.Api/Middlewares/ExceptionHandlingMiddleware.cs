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
            LogException(context, ex);
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Records the failure at a level that matches what it means.
    ///
    /// <para>
    /// A rejected validation and a database that disappeared both arrive here as exceptions, and
    /// logging them alike makes the error log useless — the one entry worth waking up for is buried
    /// under a day of callers mistyping a field. The mapping mirrors the status code chosen below,
    /// so a log level and an HTTP response never disagree about how bad something was.
    /// </para>
    ///
    /// <para>
    /// The correlation id is not written into the message: the middleware that opened the request
    /// pushed it onto the log context, so it is already a property of this event — and of every
    /// other event from the same request.
    /// </para>
    /// </summary>
    private void LogException(HttpContext context, Exception ex)
    {
        var level = ex switch
        {
            NotFoundException => LogLevel.Warning,
            DomainException => LogLevel.Warning,
            FluentValidation.ValidationException => LogLevel.Warning,
            ArgumentException => LogLevel.Warning,
            UnauthorizedAccessException => LogLevel.Warning,
            _ => LogLevel.Error
        };

        _logger.Log(
            level,
            ex,
            "[Request] {RequestMethod} {RequestPath} failed with {ErrorType}: {ErrorMessage}",
            context.Request.Method,
            context.Request.Path.Value,
            ex.GetType().Name,
            ex.Message);
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";

        ApiResponse<object> response;

        // Handle not-found domain exceptions (must come before DomainException)
        if (ex is NotFoundException notFoundEx)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            response = ApiResponseFactory.NotFound<object>(notFoundEx.Message);
        }
        // Handle other domain exceptions
        else if (ex is DomainException domainEx)
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

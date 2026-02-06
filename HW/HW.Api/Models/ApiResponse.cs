namespace HW.Api.Models;

/// <summary>
/// Generic wrapper for all API responses following REST conventions.
/// </summary>
/// <typeparam name="T">The type of data being returned</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates whether the request was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// HTTP status code (e.g., 200, 400, 401, 500)
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Human-readable message about the result
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Collection of error messages or validation errors
    /// </summary>
    public IEnumerable<string>? Errors { get; set; }

    /// <summary>
    /// The actual data payload
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Timestamp when the response was generated (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Non-generic wrapper for simple responses without data payload
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public IEnumerable<string>? Errors { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Validation error response with detailed field information
/// </summary>
public class ValidationErrorDetail
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Factory for creating standardized API responses
/// </summary>
public static class ApiResponseFactory
{
    /// <summary>
    /// Creates a successful response with data
    /// </summary>
    public static ApiResponse<T> Success<T>(T data, string message = "Operation successful", int statusCode = 200)
    {
        return new ApiResponse<T>
        {
            Success = true,
            StatusCode = statusCode,
            Message = message,
            Data = data
        };
    }

    /// <summary>
    /// Creates a successful response without data
    /// </summary>
    public static ApiResponse Success(string message = "Operation successful", int statusCode = 200)
    {
        return new ApiResponse
        {
            Success = true,
            StatusCode = statusCode,
            Message = message
        };
    }

    /// <summary>
    /// Creates a successful response with created status (201)
    /// </summary>
    public static ApiResponse<T> Created<T>(T data, string message = "Resource created successfully")
    {
        return new ApiResponse<T>
        {
            Success = true,
            StatusCode = 201,
            Message = message,
            Data = data
        };
    }

    /// <summary>
    /// Creates an error response
    /// </summary>
    public static ApiResponse<T> Error<T>(string message, int statusCode = 400, IEnumerable<string>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            StatusCode = statusCode,
            Message = message,
            Errors = errors,
            Data = default
        };
    }

    /// <summary>
    /// Creates an error response without data
    /// </summary>
    public static ApiResponse Error(string message, int statusCode = 400, IEnumerable<string>? errors = null)
    {
        return new ApiResponse
        {
            Success = false,
            StatusCode = statusCode,
            Message = message,
            Errors = errors
        };
    }

    /// <summary>
    /// Creates a validation error response
    /// </summary>
    public static ApiResponse<T> ValidationError<T>(IEnumerable<string> errors, string message = "Validation failed")
    {
        return new ApiResponse<T>
        {
            Success = false,
            StatusCode = 422,
            Message = message,
            Errors = errors,
            Data = default
        };
    }

    /// <summary>
    /// Creates a validation error response without data
    /// </summary>
    public static ApiResponse ValidationError(IEnumerable<string> errors, string message = "Validation failed")
    {
        return new ApiResponse
        {
            Success = false,
            StatusCode = 422,
            Message = message,
            Errors = errors
        };
    }

    /// <summary>
    /// Creates an unauthorized response
    /// </summary>
    public static ApiResponse<T> Unauthorized<T>(string message = "Unauthorized access")
    {
        return new ApiResponse<T>
        {
            Success = false,
            StatusCode = 401,
            Message = message,
            Data = default
        };
    }

    /// <summary>
    /// Creates a forbidden response
    /// </summary>
    public static ApiResponse<T> Forbidden<T>(string message = "Access forbidden")
    {
        return new ApiResponse<T>
        {
            Success = false,
            StatusCode = 403,
            Message = message,
            Data = default
        };
    }

    /// <summary>
    /// Creates a not found response
    /// </summary>
    public static ApiResponse<T> NotFound<T>(string message = "Resource not found")
    {
        return new ApiResponse<T>
        {
            Success = false,
            StatusCode = 404,
            Message = message,
            Data = default
        };
    }

    /// <summary>
    /// Creates a server error response
    /// </summary>
    public static ApiResponse<T> ServerError<T>(string message = "An internal server error occurred", IEnumerable<string>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            StatusCode = 500,
            Message = message,
            Errors = errors,
            Data = default
        };
    }
}

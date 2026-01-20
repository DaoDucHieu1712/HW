   ,using FluentValidation;
using System.Text.Json;

namespace HW.Api.Middlewares
{
    public class FluentValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public FluentValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Chỉ áp dụng cho POST/PUT/PATCH
            if (context.Request.Method is not ("POST" or "PUT" or "PATCH" or "GET"))
            {
                await _next(context);
                return;
            }

            // Không có body
            if (context.Request.ContentLength is null or 0)
            {
                await _next(context);
                return;
            }

            // Enable buffering để đọc body nhiều lần
            context.Request.EnableBuffering();

            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
            {
                await _next(context);
                return;
            }

            // Lấy endpoint để biết type parameter
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
                var result = await validator.ValidateAsync(new ValidationContext<object>(dto));

                if (!result.IsValid)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";

                    var errors = result.Errors.Select(e => new
                    {
                        Field = e.PropertyName,
                        Message = e.ErrorMessage
                    });

                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        Success = false,
                        Errors = errors
                    }));

                    return; // ❌ Không chạy tiếp pipeline
                }
            }

            await _next(context);
        }

    }
}

using HW.Infrastructure.MultiTenant;

namespace HW.Api.Middlewares;

public class PermissionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly UserInfo _userInfo;

    public PermissionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {

    }
}

using System.Security.Claims;
using Serilog.Context;

namespace HW.Api.Middlewares;

/// <summary>
/// Adds the authenticated caller to the log context for the rest of the request.
///
/// <para>
/// The request-summary log already records the user, but that event is written when the request
/// ends. This puts the same properties on every line in between — the handler's logs, the domain
/// event it raised, the warning from three layers down — so "what did this user do" is one query
/// rather than a correlation id looked up first and searched for second.
/// </para>
///
/// <para>
/// Must be registered after <c>UseAuthentication</c>: before it, the principal is still anonymous
/// and every request would be attributed to nobody.
/// </para>
/// </summary>
public sealed class UserContextLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public UserContextLoggingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var identity = context.User.Identity;

        if (identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        using (LogContext.PushProperty("UserId", context.User.FindFirstValue(ClaimTypes.NameIdentifier)))
        using (LogContext.PushProperty("UserName", identity.Name))
        {
            await _next(context);
        }
    }
}

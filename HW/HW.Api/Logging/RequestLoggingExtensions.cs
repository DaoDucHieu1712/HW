using Serilog;
using Serilog.Events;

namespace HW.Api.Logging;

/// <summary>
/// One summary log line per HTTP request, carrying the method, route, status, duration and who made
/// it.
///
/// <para>
/// This replaces ASP.NET Core's own request logging — three lines per request, unstructured, at
/// Information — with a single event whose properties can actually be queried: in Seq, "every
/// request over 2 seconds", "every 500 this user hit", or "the slowest endpoint this hour" are all
/// filters over this one event rather than a text search.
/// </para>
/// </summary>
public static class RequestLoggingExtensions
{
    /// <summary>Above this, a request is logged at Warning even though it succeeded.</summary>
    private const double SlowRequestThresholdMs = 1_000;

    /// <summary>
    /// Endpoints that are noise at Information: polled or hit on every page load, and interesting
    /// only when they fail — which the status-code rules below still catch.
    /// </summary>
    private static readonly string[] QuietPaths =
        ["/health", "/swagger", "/favicon.ico", "/_framework"];

    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
        => app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.GetLevel = GetLevel;
            options.EnrichDiagnosticContext = EnrichRequest;
        });

    /// <summary>
    /// Severity from the outcome rather than a fixed level, so the log can be read by level alone:
    /// anything at Warning or above is a request that failed or was slow enough to matter.
    /// </summary>
    private static LogEventLevel GetLevel(HttpContext context, double elapsedMs, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            return LogEventLevel.Error;

        // 4xx is the caller's mistake, not the server's — worth seeing, not worth paging on.
        if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
            return LogEventLevel.Warning;

        if (elapsedMs > SlowRequestThresholdMs)
            return LogEventLevel.Warning;

        return IsQuiet(context.Request.Path) ? LogEventLevel.Debug : LogEventLevel.Information;
    }

    /// <summary>
    /// Attaches the context that turns a status code into a diagnosis: which route matched, which
    /// user called it, and from where.
    /// </summary>
    private static void EnrichRequest(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        var request = context.Request;

        diagnosticContext.Set("RequestHost", request.Host.Value);
        diagnosticContext.Set("RequestScheme", request.Scheme);
        diagnosticContext.Set("Protocol", request.Protocol);
        diagnosticContext.Set("ClientIp", context.Connection.RemoteIpAddress?.ToString());
        diagnosticContext.Set("CorrelationId", context.TraceIdentifier);

        if (request.QueryString.HasValue)
            diagnosticContext.Set("QueryString", request.QueryString.Value);

        if (request.Headers.TryGetValue("User-Agent", out var userAgent))
            diagnosticContext.Set("UserAgent", userAgent.ToString());

        // The matched endpoint, not the raw path: "/api/blogs/{id}" groups in Seq where
        // "/api/blogs/7f3c…" produces one bucket per blog.
        var endpoint = context.GetEndpoint();
        if (endpoint is not null)
            diagnosticContext.Set("Endpoint", endpoint.DisplayName);

        // Enriched here rather than in middleware because authentication has completed by the time
        // this runs — earlier in the pipeline the principal is still anonymous.
        if (context.User.Identity?.IsAuthenticated == true)
        {
            diagnosticContext.Set("UserName", context.User.Identity.Name);
            diagnosticContext.Set("UserId", context.User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        }

        // Response content type is how you tell an API 404 from the SPA fallback serving HTML.
        if (!string.IsNullOrEmpty(context.Response.ContentType))
            diagnosticContext.Set("ResponseContentType", context.Response.ContentType);
    }

    private static bool IsQuiet(PathString path)
        => QuietPaths.Any(quiet => path.StartsWithSegments(quiet, StringComparison.OrdinalIgnoreCase));
}

using Serilog.Context;

namespace HW.Api.Middlewares;

/// <summary>
/// Gives every request one id that appears on every log line it produces, and hands that id back to
/// the caller.
///
/// <para>
/// The id is taken from the inbound <c>X-Correlation-ID</c> header when a caller supplies one, so a
/// trace started upstream continues here instead of restarting — which is what makes a single
/// search in Seq return the whole path of a request across services.
/// </para>
///
/// <para>
/// It is written back into <see cref="HttpContext.TraceIdentifier"/> rather than kept beside it,
/// because the SQL trace interceptor and the in-memory log buffer behind <c>/api/diagnostics</c>
/// already key off that property. One assignment brings them onto the same id as the log, so a
/// correlation id found in Seq can be pasted straight into the diagnostics endpoint.
/// </para>
///
/// <para>
/// Serilog's own <c>RequestId</c> property is not this id: ASP.NET Core opens its logging scope
/// before any middleware runs, capturing the framework-generated identifier. The two agree unless
/// a caller supplied a header, and <c>CorrelationId</c> is the one to search on either way.
/// </para>
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.TraceIdentifier = correlationId;

        // Set before the response starts: once the first byte is written the headers are sealed,
        // and a downstream component may write at any point after this.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Pushed onto the ambient Serilog context so that everything logged inside this request —
        // including code with no access to HttpContext, such as a MediatR handler or an EF
        // interceptor — carries the id without having to be passed it.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// A caller-supplied id is trusted only as far as its shape: it is echoed into every log line
    /// and into a response header, so an unbounded or newline-bearing value would be a log-forging
    /// vector. Anything unreasonable is replaced rather than sanitised.
    /// </summary>
    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var supplied))
        {
            var candidate = supplied.ToString();

            if (IsAcceptable(candidate)) return candidate;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsAcceptable(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 128
           && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ':' or '.');
}

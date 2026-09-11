using System.Diagnostics;
using FluentValidation;
using HW.Application.CQRS;
using HW.Application.Logging;
using HW.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HW.Application.Behaviors;

/// <summary>
/// Traces every command and query through the pipeline: what was asked, how long it took, and how
/// it ended.
///
/// <para>
/// The properties are attached as a logging scope rather than written into the messages. Everything
/// logged underneath — the handler, the repository, the SQL EF Core emits, an exception three
/// layers down — inherits them, so in Seq one filter on <c>RequestName</c> returns the whole story
/// of a use case instead of the two lines this class writes itself.
/// </para>
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Above this a successful request is still logged at Warning. Chosen so the level alone
    /// separates "the system worked" from "the system worked but something is wrong with it".
    /// </summary>
    private const long SlowRequestThresholdMs = 500;

    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var kind = request is IBaseCommand ? "Command" : "Query";

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestName"] = name,
            ["RequestKind"] = kind,

            // Distinguishes two runs of the same request inside one HTTP call — a query issued by a
            // handler, an agent tool invoking a second command — which the correlation id alone
            // cannot do.
            ["MediatorRequestId"] = Guid.NewGuid().ToString("N")
        });

        _logger.LogInformation("[{RequestKind}] Handling {RequestName}", kind, name);

        // Payloads are Debug-only and redacted. Even masked, a payload is the bulkiest thing this
        // pipeline can write, so it is off in production and one configuration change away in an
        // environment where it is needed.
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("[{RequestKind}] {RequestName} payload {@Payload}",
                kind, name, SensitiveDataRedactor.Describe(request));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            _logger.Log(
                stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs ? LogLevel.Warning : LogLevel.Information,
                "[{RequestKind}] Handled {RequestName} in {ElapsedMilliseconds} ms",
                kind, name, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Rethrown untouched: this observes the failure, the API's exception middleware decides
            // what the caller sees. Swallowing here would turn a failed command into a silent one.
            _logger.Log(
                LevelFor(ex),
                ex,
                "[{RequestKind}] {RequestName} failed after {ElapsedMilliseconds} ms — {ErrorType}: {ErrorMessage}",
                kind, name, stopwatch.ElapsedMilliseconds, ex.GetType().Name, ex.Message);

            throw;
        }
    }

    /// <summary>
    /// Expected refusals are not defects. A rejected validation or a missing record is the pipeline
    /// working as designed, and logging those at Error is how an error log becomes something nobody
    /// reads.
    /// </summary>
    private static LogLevel LevelFor(Exception exception) => exception switch
    {
        ValidationException => LogLevel.Warning,
        NotFoundException => LogLevel.Warning,
        DomainException => LogLevel.Warning,
        UnauthorizedAccessException => LogLevel.Warning,

        // A cancelled request is the caller hanging up, not a fault.
        OperationCanceledException => LogLevel.Information,
        _ => LogLevel.Error
    };
}

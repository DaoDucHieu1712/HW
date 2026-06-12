using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HW.Application.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;

        _logger.LogInformation("[MediatR] Start  — {RequestName} {@Request}", name, request);

        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        _logger.LogInformation("[MediatR] Done   — {RequestName} ({Elapsed}ms)", name, sw.ElapsedMilliseconds);

        return response;
    }
}

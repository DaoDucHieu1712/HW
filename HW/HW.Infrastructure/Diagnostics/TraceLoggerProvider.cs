using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;
using HW.Agentic.Abstractions.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HW.Infrastructure.Diagnostics;

public sealed class DiagnosticsOptions
{
    /// <summary>Turns the whole capture off — no interceptor writes, no log writes, empty buffers.</summary>
    public bool Enabled { get; set; } = true;

    public bool CaptureSql { get; set; } = true;

    /// <summary>
    /// Include bound parameter values with each captured command. Off by default: those values are
    /// live application data, and the buffer is readable through the agent tools.
    /// </summary>
    public bool CaptureSqlParameters { get; set; }

    [Range(10, 10_000)]
    public int SqlCapacity { get; set; } = 500;

    [Range(10, 20_000)]
    public int LogCapacity { get; set; } = 1_000;

    /// <summary>
    /// Lowest level captured into the buffer. Information by default — the MediatR logging behavior
    /// logs at that level, and losing it would take the request timeline with it.
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";
}

/// <summary>
/// Mirrors the application's log output into <see cref="LogTraceStore"/>, so the log an agent reads
/// is the same one the console shows.
///
/// <para>
/// A logging provider rather than a wrapper around <c>ILogger</c>: this catches what framework and
/// library code logs too, which is where the interesting failures usually surface.
/// </para>
/// </summary>
public sealed class TraceLoggerProvider : ILoggerProvider
{
    private static readonly string[] LevelNames =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    private readonly ConcurrentDictionary<string, TraceLogger> _loggers = new();
    private readonly LogTraceStore _store;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly LogLevel _minimum;
    private readonly bool _enabled;

    public TraceLoggerProvider(
        LogTraceStore store,
        IHttpContextAccessor httpContextAccessor,
        DiagnosticsOptions options)
    {
        _store = store;
        _httpContextAccessor = httpContextAccessor;
        _enabled = options.Enabled;
        _minimum = Enum.TryParse<LogLevel>(options.MinimumLevel, ignoreCase: true, out var level)
            ? level
            : LogLevel.Information;
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new TraceLogger(name, this));

    public void Dispose() => _loggers.Clear();

    private sealed class TraceLogger(string category, TraceLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
            => provider._enabled && logLevel >= provider._minimum && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // A logging provider must never throw: an exception here would surface from whatever
            // line of application code happened to be logging, which is close to impossible to
            // diagnose. Losing a diagnostic entry is the cheaper failure.
            try
            {
                provider._store.Record(
                    provider._httpContextAccessor.HttpContext?.TraceIdentifier,
                    LevelNames[(int)logLevel],
                    category,
                    formatter(state, exception),
                    exception?.ToString());
            }
            catch
            {
                // Deliberately swallowed — see above.
            }
        }
    }
}

/// <summary>
/// Correlation id for the request in flight.
///
/// <para>
/// ASP.NET Core's <c>TraceIdentifier</c> is reused rather than invented: the framework already
/// stamps it on every request, and it is what appears in its own diagnostics — so an id an agent
/// reports back can be matched against the real logs outside this buffer.
/// </para>
/// </summary>
public sealed class HttpCorrelationAccessor(IHttpContextAccessor httpContextAccessor) : ICorrelationAccessor
{
    public string? CorrelationId => httpContextAccessor.HttpContext?.TraceIdentifier;
}

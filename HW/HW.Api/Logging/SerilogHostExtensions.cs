using Serilog;
using Serilog.Events;
using Serilog.Exceptions;

namespace HW.Api.Logging;

/// <summary>
/// Wires Serilog as the application's logging pipeline and points it at Seq.
///
/// <para>
/// Serilog lives here, in the composition root, and nowhere else. Application, Infrastructure and
/// Domain code logs through <c>ILogger&lt;T&gt;</c> — the enrichment and the destination are a
/// hosting concern, and keeping them here means swapping Seq for anything else touches one project.
/// </para>
/// </summary>
public static class SerilogHostExtensions
{
    /// <summary>
    /// Minimum level used before configuration has been read, and the fallback floor if the
    /// <c>Serilog</c> section is missing entirely.
    /// </summary>
    private const LogEventLevel FallbackMinimumLevel = LogEventLevel.Information;

    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}    {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Installs a logger that works before the host exists.
    ///
    /// <para>
    /// Without this, anything that fails during <c>builder.Build()</c> — a bad connection string, a
    /// failed options validation, an unreachable broker — is reported by the default provider and
    /// never reaches Seq. Startup is exactly when a log is worth the most, so it is covered first
    /// and replaced by the real logger once configuration is available.
    /// </para>
    /// </summary>
    public static void CreateBootstrapLogger()
        => Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(FallbackMinimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: ConsoleTemplate)
            .CreateBootstrapLogger();

    /// <summary>
    /// Replaces the host's logging with Serilog: levels and the console/file sinks come from the
    /// <c>Serilog</c> configuration section, enrichment is applied in code, and Seq is wired from
    /// its own <c>Seq</c> section.
    /// </summary>
    public static IHostBuilder AddSerilogLogging(this IHostBuilder host, IServiceCollection services)
    {
        services.AddOptions<SeqOptions>()
            .BindConfiguration(SeqOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return host.UseSerilog(
            (context, provider, logger) => Configure(logger, context.Configuration, context.HostingEnvironment),

            // The trace ring buffer behind /api/diagnostics and the agent tools is an ILoggerProvider,
            // and Serilog would otherwise take ownership of the pipeline and leave it receiving
            // nothing. Forwarding keeps both readers of the same log alive.
            writeToProviders: true);
    }

    private static void Configure(
        LoggerConfiguration logger,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        logger
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()

            // Unwraps an exception into structured properties — inner exceptions, the SQL error
            // number, the failing command — instead of one flat string that has to be read by eye.
            .Enrich.WithExceptionDetails()

            // Seq groups by these, which is what makes one instance usable for several services and
            // for several copies of this one.
            .Enrich.WithProperty("Application", environment.ApplicationName)
            .Enrich.WithProperty("Environment", environment.EnvironmentName);

        // A configuration file with no sinks would start the app and log nothing anywhere, which
        // looks identical to the app not running. Fall back to the console instead.
        if (!configuration.GetSection("Serilog:WriteTo").GetChildren().Any())
        {
            logger
                .MinimumLevel.Is(FallbackMinimumLevel)
                .WriteTo.Console(outputTemplate: ConsoleTemplate);
        }

        AddSeq(logger, configuration);
    }

    private static void AddSeq(LoggerConfiguration logger, IConfiguration configuration)
    {
        var options = configuration.GetSection(SeqOptions.SectionName).Get<SeqOptions>() ?? new SeqOptions();

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            // Said out loud, because "no logs in Seq" is otherwise indistinguishable from a broken
            // sink, and this is the one message that tells the two apart.
            Log.Information("[Logging] Seq sink disabled — logs go to the console and file sinks only");
            return;
        }

        logger.WriteTo.Seq(
            serverUrl: options.ServerUrl,
            restrictedToMinimumLevel: ParseLevel(options.MinimumLevel),
            apiKey: string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey,

            // Durable buffering when a path is configured: events survive Seq being down and are
            // replayed when it comes back. Without one the sink drops what it cannot deliver.
            bufferBaseFilename: string.IsNullOrWhiteSpace(options.BufferPath) ? null : options.BufferPath,

            // The sink is asynchronous and never blocks the request thread. This bounds what an
            // unreachable Seq may cost in memory before events start being discarded — a logging
            // outage must not become an application outage.
            queueSizeLimit: options.BufferSize);
    }

    private static LogEventLevel ParseLevel(string? level)
        => Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out var parsed)
            ? parsed
            : FallbackMinimumLevel;
}

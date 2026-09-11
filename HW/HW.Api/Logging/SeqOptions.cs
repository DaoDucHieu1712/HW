using System.ComponentModel.DataAnnotations;

namespace HW.Api.Logging;

/// <summary>
/// Where structured logs are shipped for querying.
///
/// <para>
/// Kept as its own configuration section rather than an entry in <c>Serilog:WriteTo</c>: the server
/// URL and API key are the two things that change per environment, and a flat section maps onto the
/// <c>Seq__ServerUrl</c> / <c>Seq__ApiKey</c> environment variables a container is configured with —
/// where the array form would need an index nobody can be expected to keep correct.
/// </para>
/// </summary>
public sealed class SeqOptions
{
    public const string SectionName = "Seq";

    /// <summary>
    /// Off leaves the console and file sinks running. Seq is a convenience for a developer or an
    /// operator, never a dependency the application needs in order to start.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The Seq ingestion endpoint — the same address its UI is served from. Blank disables the sink
    /// as surely as <see cref="Enabled"/> does, which is why this carries no <c>[Url]</c>
    /// attribute: validating it would turn "no Seq configured" into a startup failure.
    /// </summary>
    public string ServerUrl { get; set; } = "http://localhost:5341";

    /// <summary>Empty for an unsecured local instance; required once Seq has API keys configured.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Floor for what reaches Seq, independent of the console. Debug here with Information on the
    /// console is the usual arrangement: the searchable sink gets the detail, the terminal stays
    /// readable.
    /// </summary>
    public string MinimumLevel { get; set; } = "Debug";

    /// <summary>
    /// Events buffered in memory before the sink starts dropping them. The sink is asynchronous, so
    /// this is the cost of Seq being briefly unreachable — not a limit on throughput.
    /// </summary>
    [Range(100, 100_000)]
    public int BufferSize { get; set; } = 10_000;

    /// <summary>
    /// Spools events to disk while Seq is down and replays them when it returns. Off by default:
    /// it writes to the local filesystem, which is not always somewhere the app may write.
    /// </summary>
    public string? BufferPath { get; set; }
}

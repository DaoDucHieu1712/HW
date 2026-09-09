using System.Text.Json;
using HW.Application.Abstractions.Diagnostics;

namespace HW.Application.Agents.Tools;

/// <summary>
/// Recent application log entries, including the exceptions the pipeline swallowed into an HTTP
/// response. Paired with <see cref="TraceSqlTool"/> through the correlation id: find the failing
/// request here, then read the SQL it issued.
/// </summary>
public sealed class TraceLogTool : AgentToolBase
{
    private readonly ILogTraceStore _store;

    public TraceLogTool(ILogTraceStore store, AgentLoopOptions options) : base(options)
        => _store = store;

    public override string Name => "trace_log";

    public override string Description =>
        "Read this application's recent log entries, newest first, with level, category, message, " +
        "correlation id, and full exception text where there was one. Start a bug investigation " +
        "here: filter minLevel to Error to find failures, then take the correlationId from an entry " +
        "and pass it to trace_sql to see what that same request did against the database. This is a " +
        "bounded in-memory buffer of recent activity, not the full log history.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "correlationId": {
              "type": "string",
              "description": "Only entries from this request. Use it to follow one request end to end."
            },
            "contains": {
              "type": "string",
              "description": "Case-insensitive substring of the message or exception text."
            },
            "minLevel": {
              "type": "string",
              "description": "Lowest severity to include.",
              "enum": ["Trace", "Debug", "Information", "Warning", "Error", "Critical"]
            },
            "category": {
              "type": "string",
              "description": "Substring of the logger category, e.g. a class or namespace name."
            },
            "limit": {
              "type": "integer",
              "description": "How many to return. Defaults to 25."
            }
          },
          "additionalProperties": false
        }
        """;

    public override Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var limit = Math.Clamp(OptionalInt(input, "limit") ?? 25, 1, Options.MaxToolResultItems);

        var entries = _store.Query(new LogTraceQuery(
            OptionalString(input, "correlationId"),
            OptionalString(input, "contains"),
            OptionalString(input, "minLevel"),
            OptionalString(input, "category"),
            limit));

        var (count, capacity) = _store.Stats;

        if (entries.Count == 0)
        {
            return Task.FromResult(count == 0
                ? "No log entries have been captured yet."
                : $"No captured log entries match those filters. The buffer holds {count} entries; widen the filters.");
        }

        return Task.FromResult(Serialize(new
        {
            matched = entries.Count,
            bufferHolds = count,
            bufferCapacity = capacity,
            entries,
        }));
    }
}

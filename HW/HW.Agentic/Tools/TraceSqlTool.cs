using System.Text.Json;
using HW.Agentic.Abstractions.Diagnostics;
using HW.Agentic.Core;

namespace HW.Agentic.Tools;

/// <summary>
/// The database commands the app actually ran, captured by an EF Core interceptor. This is the tool
/// that separates "the code looks like it should do one query" from "it did forty".
/// </summary>
public sealed class TraceSqlTool : AgentToolBase
{
    private readonly ISqlTraceStore _store;

    public TraceSqlTool(ISqlTraceStore store, AgentLoopOptions options) : base(options)
        => _store = store;

    public override string Name => "trace_sql";

    public override string Description =>
        "Read the SQL this application recently executed, newest first, with timing and any error. " +
        "Use it to see what a request really did against the database: N+1 query storms, a missing " +
        "filter, a slow command, a failed one. Filter by correlationId to get exactly one request's " +
        "commands, by minElapsedMs to find slow ones, or failedOnly to find errors. This is a " +
        "bounded in-memory buffer of recent activity — it does not go back further than that.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "correlationId": {
              "type": "string",
              "description": "Only commands issued by this request. Correlation ids appear on log entries from trace_log."
            },
            "contains": {
              "type": "string",
              "description": "Case-insensitive substring of the SQL text, e.g. a table name."
            },
            "minElapsedMs": {
              "type": "integer",
              "description": "Only commands that took at least this many milliseconds."
            },
            "failedOnly": {
              "type": "boolean",
              "description": "True to return only commands that threw."
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

        var entries = _store.Query(new SqlTraceQuery(
            OptionalString(input, "correlationId"),
            OptionalString(input, "contains"),
            OptionalLong(input, "minElapsedMs"),
            OptionalBool(input, "failedOnly"),
            limit));

        var (count, capacity) = _store.Stats;

        if (entries.Count == 0)
        {
            // An empty result has two very different causes, and an agent that cannot tell them
            // apart will confidently conclude the query never ran.
            return Task.FromResult(count == 0
                ? "No SQL has been captured yet — the app has not run a database command since it started."
                : $"No captured SQL matches those filters. The buffer holds {count} commands; widen the filters.");
        }

        return Task.FromResult(Serialize(new
        {
            matched = entries.Count,
            bufferHolds = count,
            bufferCapacity = capacity,
            commands = entries,
        }));
    }
}
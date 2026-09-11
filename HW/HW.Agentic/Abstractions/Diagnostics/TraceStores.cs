namespace HW.Agentic.Abstractions.Diagnostics;

/// <summary>
/// One database command the app actually ran.
/// </summary>
/// <param name="CorrelationId">
/// The request that issued it, so a log line and the SQL it caused can be lined up. Null for work
/// outside a request — the outbox processor, a hosted service.
/// </param>
/// <param name="Parameters">
/// Rendered parameter values. Present because a slow or wrong query is usually only explicable with
/// the values bound to it — and absent when parameter capture is switched off, since those values
/// are production data.
/// </param>
public sealed record SqlTraceEntry(
    long Id,
    DateTimeOffset At,
    string? CorrelationId,
    string Operation,
    string Sql,
    string? Parameters,
    long ElapsedMs,
    bool Failed,
    string? Error);

public sealed record LogTraceEntry(
    long Id,
    DateTimeOffset At,
    string? CorrelationId,
    string Level,
    string Category,
    string Message,
    string? Exception);

/// <param name="Contains">Case-insensitive substring match over the SQL text.</param>
/// <param name="MinElapsedMs">Only commands at least this slow — the usual way in to a latency bug.</param>
public sealed record SqlTraceQuery(
    string? CorrelationId = null,
    string? Contains = null,
    long? MinElapsedMs = null,
    bool FailedOnly = false,
    int Limit = 50);

/// <param name="MinLevel">Trace, Debug, Information, Warning, Error, or Critical.</param>
public sealed record LogTraceQuery(
    string? CorrelationId = null,
    string? Contains = null,
    string? MinLevel = null,
    string? Category = null,
    int Limit = 50);

/// <summary>
/// A bounded, in-memory record of recent activity, kept so an agent can be pointed at what the app
/// actually did rather than at what the code suggests it does.
///
/// <para>
/// Deliberately a ring buffer in process memory: it is a debugging aid, not an audit log. It holds
/// only the most recent entries, it is lost on restart, and it is not shared between instances —
/// anything that must survive those belongs in real log storage.
/// </para>
/// </summary>
public interface ITraceStore<TEntry, TQuery>
{
    void Add(TEntry entry);

    /// <summary>Newest first, capped by the query's limit.</summary>
    IReadOnlyList<TEntry> Query(TQuery query);

    /// <summary>Entries currently held, and the ceiling before the oldest are dropped.</summary>
    (int Count, int Capacity) Stats { get; }

    void Clear();
}

public interface ISqlTraceStore : ITraceStore<SqlTraceEntry, SqlTraceQuery>;

public interface ILogTraceStore : ITraceStore<LogTraceEntry, LogTraceQuery>;

/// <summary>
/// The id tying a log line, a SQL command, and an agent run to the same inbound request.
/// </summary>
public interface ICorrelationAccessor
{
    string? CorrelationId { get; }
}

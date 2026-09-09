using HW.Application.Abstractions.Diagnostics;

namespace HW.Infrastructure.Diagnostics;

/// <summary>
/// A fixed-capacity, newest-wins buffer of recent activity.
///
/// <para>
/// Bounded by design. This is written to on every database command and every log line, so it has to
/// cost almost nothing and it must never grow: an unbounded diagnostic buffer is a memory leak that
/// only shows up under the load you least want it to.
/// </para>
///
/// <para>
/// One lock rather than a lock-free structure. Contention is on a queue operation measured in
/// nanoseconds, against database and HTTP work measured in milliseconds, so the simple version is
/// both fast enough and obviously correct — which matters more for something that runs inside every
/// request.
/// </para>
/// </summary>
public abstract class RingTraceStore<TEntry, TQuery>(int capacity) : ITraceStore<TEntry, TQuery>
{
    private readonly Queue<TEntry> _entries = new(capacity);
    private readonly object _gate = new();
    private long _nextId;

    protected int Capacity { get; } = Math.Max(capacity, 1);

    public (int Count, int Capacity) Stats
    {
        get
        {
            lock (_gate) return (_entries.Count, Capacity);
        }
    }

    public void Add(TEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);

            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    public IReadOnlyList<TEntry> Query(TQuery query)
    {
        // Snapshot under the lock, then filter outside it: filtering runs a regex-free but still
        // per-entry predicate, and holding the lock across it would block the request threads
        // writing into the buffer.
        TEntry[] snapshot;

        lock (_gate) snapshot = [.. _entries];

        return snapshot
            .Reverse()
            .Where(entry => Matches(entry, query))
            .Take(Math.Max(LimitOf(query), 1))
            .ToList();
    }

    /// <summary>Ids are assigned here so entries stay orderable after the oldest have been dropped.</summary>
    protected long NextId() => Interlocked.Increment(ref _nextId);

    protected abstract bool Matches(TEntry entry, TQuery query);

    protected abstract int LimitOf(TQuery query);

    protected static bool Contains(string? haystack, string? needle)
        => string.IsNullOrEmpty(needle)
           || (haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

public sealed class SqlTraceStore(int capacity)
    : RingTraceStore<SqlTraceEntry, SqlTraceQuery>(capacity), ISqlTraceStore
{
    public SqlTraceEntry Record(
        string? correlationId,
        string operation,
        string sql,
        string? parameters,
        long elapsedMs,
        bool failed,
        string? error)
    {
        var entry = new SqlTraceEntry(
            NextId(), DateTimeOffset.UtcNow, correlationId, operation, sql, parameters, elapsedMs, failed, error);

        Add(entry);
        return entry;
    }

    protected override int LimitOf(SqlTraceQuery query) => query.Limit;

    protected override bool Matches(SqlTraceEntry entry, SqlTraceQuery query)
        => (query.CorrelationId is null || string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.OrdinalIgnoreCase))
           && Contains(entry.Sql, query.Contains)
           && (query.MinElapsedMs is null || entry.ElapsedMs >= query.MinElapsedMs)
           && (!query.FailedOnly || entry.Failed);
}

public sealed class LogTraceStore(int capacity)
    : RingTraceStore<LogTraceEntry, LogTraceQuery>(capacity), ILogTraceStore
{
    /// <summary>Ordered by severity so a <c>minLevel</c> filter can compare them.</summary>
    private static readonly string[] Levels =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    public void Record(string? correlationId, string level, string category, string message, string? exception)
        => Add(new LogTraceEntry(
            NextId(), DateTimeOffset.UtcNow, correlationId, level, category, message, exception));

    protected override int LimitOf(LogTraceQuery query) => query.Limit;

    protected override bool Matches(LogTraceEntry entry, LogTraceQuery query)
        => (query.CorrelationId is null || string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.OrdinalIgnoreCase))
           && Contains(entry.Category, query.Category)
           && (Contains(entry.Message, query.Contains) || Contains(entry.Exception, query.Contains))
           && Rank(entry.Level) >= Rank(query.MinLevel);

    /// <summary>
    /// An unrecognised level sorts to the bottom, so a filter naming one nobody logs at does not
    /// silently hide everything.
    /// </summary>
    private static int Rank(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return -1;

        var index = Array.FindIndex(Levels, name => name.Equals(level, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? -1 : index;
    }
}

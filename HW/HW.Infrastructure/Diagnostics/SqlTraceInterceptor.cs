using System.Data.Common;
using System.Text;
using HW.Application.Abstractions.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HW.Infrastructure.Diagnostics;

/// <summary>
/// Captures every database command EF Core executes into <see cref="SqlTraceStore"/>, so an agent
/// can be shown what the application actually ran rather than what the LINQ suggests it would.
///
/// <para>
/// EF gives the elapsed time on the <c>*Executed</c> callbacks, so the duration here is real rather
/// than measured around the call. Both the success and failure paths are handled: a command that
/// threw is the single most useful thing in the buffer, and it never reaches an <c>Executed</c>
/// callback.
/// </para>
/// </summary>
public sealed class SqlTraceInterceptor : DbCommandInterceptor
{
    private readonly SqlTraceStore _store;
    private readonly ICorrelationAccessor _correlation;
    private readonly DiagnosticsOptions _options;

    public SqlTraceInterceptor(
        SqlTraceStore store,
        ICorrelationAccessor correlation,
        DiagnosticsOptions options)
    {
        _store = store;
        _correlation = correlation;
        _options = options;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Capture("Reader", command, eventData.Duration, null);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default)
    {
        Capture("Reader", command, eventData.Duration, null);
        return base.ReaderExecutedAsync(command, eventData, result, ct);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Capture("NonQuery", command, eventData.Duration, null);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
    {
        Capture("NonQuery", command, eventData.Duration, null);
        return base.NonQueryExecutedAsync(command, eventData, result, ct);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Capture("Scalar", command, eventData.Duration, null);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken ct = default)
    {
        Capture("Scalar", command, eventData.Duration, null);
        return base.ScalarExecutedAsync(command, eventData, result, ct);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        Capture("Failed", command, eventData.Duration, eventData.Exception);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken ct = default)
    {
        Capture("Failed", command, eventData.Duration, eventData.Exception);
        return base.CommandFailedAsync(command, eventData, ct);
    }

    private void Capture(string operation, DbCommand command, TimeSpan duration, Exception? exception)
    {
        if (!_options.CaptureSql) return;

        _store.Record(
            _correlation.CorrelationId,
            operation,
            command.CommandText,
            _options.CaptureSqlParameters ? RenderParameters(command) : null,
            (long)duration.TotalMilliseconds,
            exception is not null,
            exception?.Message);
    }

    /// <summary>
    /// Parameter values are production data — names, emails, whatever the app stores — and this
    /// buffer is readable by an agent and, through it, by whoever prompts one. Capture is therefore
    /// off unless a developer switches it on, which is a decision worth making deliberately.
    /// </summary>
    private static string RenderParameters(DbCommand command)
    {
        if (command.Parameters.Count == 0) return string.Empty;

        var builder = new StringBuilder();

        foreach (DbParameter parameter in command.Parameters)
        {
            if (builder.Length > 0) builder.Append(", ");

            builder.Append(parameter.ParameterName).Append('=')
                .Append(parameter.Value is null or DBNull ? "NULL" : parameter.Value);
        }

        return builder.ToString();
    }
}

using HW.Api.Models;
using HW.Application.Abstractions.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HW.Api.Controllers;

/// <summary>
/// The same trace buffers the agents read, exposed for a human.
///
/// <para>
/// Worth having beyond the agents: when an agent reports a correlation id, this is where you check
/// its claim against the raw entries — and when it says the buffer held nothing, this is where you
/// confirm that rather than assume.
/// </para>
/// </summary>
[Route("api/diagnostics")]
[ApiController]
public class DiagnosticsController : ControllerBase
{
    private readonly ISqlTraceStore _sql;
    private readonly ILogTraceStore _logs;

    public DiagnosticsController(ISqlTraceStore sql, ILogTraceStore logs)
    {
        _sql = sql;
        _logs = logs;
    }

    /// <summary>Recent database commands, newest first, with timings and errors.</summary>
    [HttpGet("sql")]
    public IActionResult GetSql(
        [FromQuery] string? correlationId,
        [FromQuery] string? contains,
        [FromQuery] long? minElapsedMs,
        [FromQuery] bool failedOnly = false,
        [FromQuery] int limit = 50)
    {
        var entries = _sql.Query(new SqlTraceQuery(correlationId, contains, minElapsedMs, failedOnly, limit));
        var (count, capacity) = _sql.Stats;

        return Ok(ApiResponseFactory.Success(new { bufferHolds = count, bufferCapacity = capacity, entries }));
    }

    /// <summary>Recent log entries, newest first, with full exception text.</summary>
    [HttpGet("logs")]
    public IActionResult GetLogs(
        [FromQuery] string? correlationId,
        [FromQuery] string? contains,
        [FromQuery] string? minLevel,
        [FromQuery] string? category,
        [FromQuery] int limit = 50)
    {
        var entries = _logs.Query(new LogTraceQuery(correlationId, contains, minLevel, category, limit));
        var (count, capacity) = _logs.Stats;

        return Ok(ApiResponseFactory.Success(new { bufferHolds = count, bufferCapacity = capacity, entries }));
    }
}

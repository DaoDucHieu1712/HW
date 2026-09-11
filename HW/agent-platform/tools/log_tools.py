"""Log analysis tool.

Reads structured events from Seq when it is configured, and falls back to
scanning local log files otherwise -- an incident should still be reconstructable
on a developer machine with nothing but the log directory.
"""

from __future__ import annotations

import re
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

from integrations.seq_client import LogEvent, SeqClient

from .base import Tool, ToolError, ToolResult

#: Recognises the leading timestamp + level of a typical .NET/Serilog line.
_LINE = re.compile(
    r"^(?P<ts>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}[.,\d]*Z?)\s*"
    r"\[?(?P<level>TRACE|DEBUG|INFO(?:RMATION)?|WARN(?:ING)?|ERROR|FATAL|CRITICAL)\]?\s*"
    r"(?P<message>.*)$",
    re.IGNORECASE,
)
_EXCEPTION = re.compile(r"^(?P<type>[\w.]+(?:Exception|Error)):\s*(?P<message>.*)$")


class AnalyzeLogsTool(Tool):
    name = "analyze_logs"
    description = (
        "Fetch and summarise error logs. Anchor on a correlation id to get the "
        "full ordered timeline of one request, or query by level and time window "
        "to find what is failing. Returns events oldest-first, plus the most "
        "frequent exception types, so cause can be told apart from consequence."
    )
    read_only = True
    tags = ("logs", "diagnostics")
    input_schema = {
        "type": "object",
        "properties": {
            "correlation_id": {
                "type": "string",
                "description": "Trace/correlation id. When given, returns that request's whole timeline.",
            },
            "query": {
                "type": "string",
                "description": "Free-text or Seq filter expression to match against messages.",
            },
            "level": {
                "type": "string",
                "description": "Minimum level to include: Error, Warning, Information.",
            },
            "since_minutes": {
                "type": "integer",
                "description": "Time window to search, in minutes (default 60).",
            },
            "limit": {"type": "integer", "description": "Maximum events to return (default 100)."},
        },
        "required": [],
    }

    def __init__(
        self,
        *,
        seq: SeqClient | None = None,
        log_dir: str | Path | None = None,
        default_limit: int = 100,
    ) -> None:
        self.seq = seq
        self.log_dir = Path(log_dir) if log_dir else None
        self.default_limit = default_limit

    def _execute(
        self,
        correlation_id: str | None = None,
        query: str | None = None,
        level: str | None = None,
        since_minutes: int | None = None,
        limit: int | None = None,
    ) -> ToolResult:
        max_events = limit or self.default_limit

        if self.seq is not None and self.seq.configured:
            events = self._from_seq(correlation_id, query, level, since_minutes, max_events)
            source = "seq"
        elif self.log_dir is not None:
            events = self._from_files(correlation_id, query, level, max_events)
            source = f"files:{self.log_dir}"
        else:
            raise ToolError(
                "no log source available: configure integrations.seq_base_url or a log directory"
            )

        return ToolResult.success(
            self.name,
            {
                "source": source,
                "count": len(events),
                "events": [event.to_dict() for event in events],
                "timeline": "\n".join(event.as_line() for event in events[:40]),
                "top_exceptions": summarise_exceptions(events),
            },
            correlation_id=correlation_id,
        )

    # -- sources ---------------------------------------------------------

    def _from_seq(
        self,
        correlation_id: str | None,
        query: str | None,
        level: str | None,
        since_minutes: int | None,
        limit: int,
    ) -> list[LogEvent]:
        assert self.seq is not None
        if correlation_id:
            return self.seq.by_correlation_id(correlation_id, limit=limit)
        return self.seq.events(
            filter_expression=query,
            level=level,
            since_minutes=since_minutes or 60,
            limit=limit,
        )

    def _from_files(
        self,
        correlation_id: str | None,
        query: str | None,
        level: str | None,
        limit: int,
    ) -> list[LogEvent]:
        if self.log_dir is None or not self.log_dir.exists():
            raise ToolError(f"log directory not found: {self.log_dir}")

        wanted_level = (level or "").upper()
        events: list[LogEvent] = []
        for path in sorted(self.log_dir.glob("*.log")):
            for event in parse_log_file(path):
                if correlation_id and correlation_id not in event.message:
                    continue
                if query and query.lower() not in event.message.lower():
                    continue
                if wanted_level and not event.level.upper().startswith(wanted_level[:4]):
                    continue
                events.append(event)
                if len(events) >= limit:
                    return events
        return events


def parse_log_file(path: Path) -> Iterable[LogEvent]:
    """Parse a plain-text log file into events, attaching stack traces.

    Continuation lines (indented, or a bare exception line) are folded into the
    event above them so a stack trace stays attached to its message.
    """
    current: LogEvent | None = None
    trace: list[str] = []

    def flush() -> LogEvent | None:
        if current is not None and trace:
            current.exception = "\n".join(trace).strip()
        return current

    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:  # pragma: no cover - unreadable file
        return

    for line in lines:
        match = _LINE.match(line.strip())
        if match:
            previous = flush()
            if previous is not None:
                yield previous
            trace = []
            current = LogEvent(
                timestamp=match.group("ts"),
                level=match.group("level").upper(),
                message=match.group("message").strip(),
                exception=None,
                correlation_id=_find_correlation_id(line),
                properties={"file": path.name},
            )
        elif current is not None and line.strip():
            trace.append(line)

    final = flush()
    if final is not None:
        yield final


_CORRELATION = re.compile(
    r"(?:correlation[_-]?id|traceid|requestid)\W{0,3}([0-9a-fA-F-]{8,})", re.IGNORECASE
)


def _find_correlation_id(line: str) -> str | None:
    match = _CORRELATION.search(line)
    return match.group(1) if match else None


def summarise_exceptions(events: Iterable[LogEvent], *, top: int = 5) -> list[dict[str, Any]]:
    """Count exception types across events, most frequent first.

    The most frequent type is often the consequence, not the cause -- the first
    one in the timeline usually matters more. Both are reported.
    """
    counter: Counter[str] = Counter()
    for event in events:
        blob = event.exception or event.message
        for line in blob.splitlines():
            match = _EXCEPTION.match(line.strip())
            if match:
                counter[match.group("type")] += 1
                break
    return [{"type": name, "count": count} for name, count in counter.most_common(top)]


__all__ = ["AnalyzeLogsTool", "parse_log_file", "summarise_exceptions"]

"""Database query tool.

Read-only by construction: the statement is parsed before execution and anything
that is not a single ``SELECT`` (or ``WITH ... SELECT``) is refused. The agent
cannot talk this check out of its decision, because the check never reads the
prompt -- only the SQL it was handed.
"""

from __future__ import annotations

import logging
import re
from typing import Any, Callable, Protocol, Sequence

from .base import Tool, ToolError, ToolResult

logger = logging.getLogger(__name__)

#: Statements that mutate data or schema. Matched as whole words.
FORBIDDEN = (
    "insert",
    "update",
    "delete",
    "drop",
    "truncate",
    "alter",
    "create",
    "grant",
    "revoke",
    "merge",
    "exec",
    "execute",
    "sp_",
    "xp_",
)
_COMMENT = re.compile(r"(--[^\n]*)|(/\*.*?\*/)", re.DOTALL)


class QueryRunner(Protocol):
    """Anything that can execute a parameterised read query.

    Keeping this a protocol means the tool has no driver dependency: production
    passes a pyodbc/SQLAlchemy-backed callable, tests pass a lambda.
    """

    def __call__(
        self, sql: str, parameters: Sequence[Any] | None = None
    ) -> list[dict[str, Any]]:  # pragma: no cover - protocol
        ...


def assert_read_only(sql: str) -> str:
    """Return the normalised statement, or raise :class:`ToolError`."""
    stripped = _COMMENT.sub(" ", sql).strip().rstrip(";").strip()
    if not stripped:
        raise ToolError("empty query")
    if ";" in stripped:
        raise ToolError("multiple statements are not allowed")

    lowered = stripped.lower()
    if not (lowered.startswith("select") or lowered.startswith("with")):
        raise ToolError("only SELECT queries are allowed")
    for keyword in FORBIDDEN:
        if re.search(rf"(^|[^\w]){re.escape(keyword)}([^\w]|$)", lowered):
            raise ToolError(f"statement contains the forbidden keyword {keyword!r}")
    return stripped


class QueryDatabaseTool(Tool):
    name = "query_database"
    description = (
        "Run a read-only SQL SELECT against the application database and return "
        "the rows. Use parameters rather than string interpolation. Only a single "
        "SELECT statement is accepted; anything that writes is refused."
    )
    read_only = True
    tags = ("database", "diagnostics")
    input_schema = {
        "type": "object",
        "properties": {
            "sql": {
                "type": "string",
                "description": "A single SELECT statement. Use ? placeholders for values.",
            },
            "parameters": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Values bound to the placeholders, in order.",
            },
            "max_rows": {"type": "integer", "description": "Row cap (default 200)."},
        },
        "required": ["sql"],
    }

    def __init__(self, runner: QueryRunner | Callable[..., Any], *, max_rows: int = 200) -> None:
        self.runner = runner
        self.max_rows = max_rows

    def _execute(
        self,
        sql: str,
        parameters: Sequence[Any] | None = None,
        max_rows: int | None = None,
    ) -> ToolResult:
        statement = assert_read_only(sql)
        limit = max_rows or self.max_rows
        try:
            rows = self.runner(statement, list(parameters or []))
        except Exception as exc:  # noqa: BLE001 - driver errors are data here
            raise ToolError(f"query failed: {type(exc).__name__}: {exc}") from exc

        rows = list(rows or [])
        truncated = len(rows) > limit
        return ToolResult.success(
            self.name,
            {"row_count": len(rows), "rows": rows[:limit], "truncated": truncated},
            sql=statement,
        )


__all__ = ["FORBIDDEN", "QueryDatabaseTool", "QueryRunner", "assert_read_only"]

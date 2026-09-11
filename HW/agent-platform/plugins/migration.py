"""Migration plugin.

Database migrations are the one change in a normal deployment that cannot be
rolled back by redeploying the previous build. This plugin adds a tool that
inspects a migration and a review pass that refuses to let a destructive or
locking migration through unnoticed.

It never *runs* a migration. Applying schema changes is a human decision.
"""

from __future__ import annotations

import re
from typing import Any

from graph.state import AgentState
from tools.base import Tool, ToolError, ToolResult

from .base import Plugin, PluginContext, PluginResult

#: ``(pattern, severity, message)`` for statements that need a human decision.
RISKY_STATEMENTS: list[tuple[re.Pattern[str], str, str]] = [
    (
        re.compile(r"\bDROP\s+(TABLE|COLUMN|SCHEMA|DATABASE)\b", re.I),
        "blocker",
        "Destructive: data is deleted and redeploying the old build will not restore it.",
    ),
    (
        re.compile(r"\bALTER\s+COLUMN\b.*\bNOT\s+NULL\b", re.I),
        "blocker",
        "Adding NOT NULL to an existing column fails if any row holds NULL. "
        "Backfill in a prior migration first.",
    ),
    (
        re.compile(r"\bALTER\s+TABLE\b.*\bALTER\s+COLUMN\b.*\b(int|bigint|varchar|nvarchar|decimal)\b", re.I),
        "major",
        "Changing a column type rewrites the table and holds a lock for its duration.",
    ),
    (
        re.compile(r"\bCREATE\s+(UNIQUE\s+)?INDEX\b(?!.*\bONLINE\b)", re.I),
        "major",
        "Index creation without ONLINE locks the table on a large dataset.",
    ),
    (
        re.compile(r"\b(UPDATE|DELETE)\b(?![\s\S]{0,400}\bWHERE\b)", re.I),
        "blocker",
        "Unqualified UPDATE or DELETE: it touches every row.",
    ),
    (
        re.compile(r"\bEXEC\s+sp_rename\b", re.I),
        "major",
        "Renaming breaks anything still referring to the old name during rollout.",
    ),
]


class InspectMigrationTool(Tool):
    """Reads a migration file and reports the risky statements in it."""

    name = "inspect_migration"
    description = (
        "Inspect a database migration file and report destructive or locking "
        "statements, so the risk is known before it is applied."
    )
    read_only = True
    tags = ("database", "migration", "review")
    input_schema = {
        "type": "object",
        "properties": {
            "path": {"type": "string", "description": "Workspace-relative migration file."}
        },
        "required": ["path"],
    }

    def __init__(self, read_file: Any) -> None:
        #: Injected reader (the registered ``read_file`` tool), so path policy
        #: is enforced in exactly one place.
        self._read = read_file

    def _execute(self, path: str) -> ToolResult:
        content = self._read(path)
        if not content:
            raise ToolError(f"could not read {path}")
        findings = scan_migration(content, path)
        return ToolResult.success(
            self.name,
            {"path": path, "risk_count": len(findings), "findings": findings},
            reversible=not any(f["severity"] == "blocker" for f in findings),
        )


def scan_migration(content: str, path: str = "") -> list[dict[str, Any]]:
    """Return the risky statements found in a migration."""
    findings: list[dict[str, Any]] = []
    for number, line in enumerate(content.splitlines(), start=1):
        stripped = line.strip()
        if not stripped or stripped.startswith(("--", "//", "#")):
            continue
        for pattern, severity, message in RISKY_STATEMENTS:
            if pattern.search(stripped):
                findings.append(
                    {
                        "path": path,
                        "line": number,
                        "severity": severity,
                        "message": message,
                        "statement": stripped[:200],
                    }
                )
                break
    return findings


class MigrationPlugin(Plugin):
    """Guards schema changes: inspect, report, never apply."""

    name = "migration"
    version = "1.0"
    description = "Reviews database migrations for destructive or locking statements."
    stages = ("review", "pre_merge")

    #: Paths that look like migrations.
    PATTERNS = ("migrations/", "migration/", "_migration", ".sql")

    def register(self, context: PluginContext) -> None:
        self._bind(context)
        self.add_tool(InspectMigrationTool(self.read))

    def validate(self) -> list[str]:
        if not self.context.tools.has("read_file"):
            return ["the read_file tool is not registered"]
        return []

    def execute(self, state: AgentState) -> PluginResult:
        migrations = [
            path
            for path in self.changed_files(state)
            if any(marker in path.lower() for marker in self.PATTERNS)
        ]
        if not migrations:
            return PluginResult(plugin=self.name, artifacts={"migrations": 0})

        findings = []
        for path in migrations:
            content = self.read(path)
            if not content:
                continue
            for item in scan_migration(content, path):
                findings.append(
                    self.finding(
                        item["message"] + f"  Statement: {item['statement']}",
                        severity=item["severity"],
                        category="correctness",
                        path=item["path"],
                        line=item["line"],
                        suggestion=(
                            "Split the change into an expand/contract pair, or state "
                            "the rollback plan explicitly in the pull request."
                        ),
                    )
                )

        return PluginResult(
            plugin=self.name,
            findings=findings,
            artifacts={"migrations": len(migrations), "migration_files": migrations},
        )


__all__ = ["InspectMigrationTool", "MigrationPlugin", "RISKY_STATEMENTS", "scan_migration"]

"""Performance review plugin.

Looks for the costs that only show up under production data volumes: queries
inside loops, unbounded reads, and synchronous blocking on async code paths.
"""

from __future__ import annotations

import re
from typing import Any, Pattern

from graph.state import AgentState

from .base import LLMReviewPlugin, PluginResult

PATTERNS: list[tuple[Pattern[str], str, str]] = [
    (
        re.compile(r"\.(Result|GetAwaiter\(\)\.GetResult\(\))\b"),
        "major",
        "Blocking on an async call. Under load this exhausts the thread pool and can deadlock.",
    ),
    (
        re.compile(r"\.Wait\(\)\s*;"),
        "major",
        "Blocking wait on a task. Await it instead.",
    ),
    (
        re.compile(r"\.ToList\(\)\s*\.\s*(Where|Select|First|Any)\b"),
        "minor",
        "Materialising before filtering pulls the whole set into memory. Filter in the query.",
    ),
    (
        re.compile(r"\bSELECT\s+\*", re.I),
        "minor",
        "SELECT * reads columns nothing uses and breaks when the schema changes.",
    ),
    (
        re.compile(r"\.Include\([^)]*\)[^;\n]*\.Include\([^)]*\)"),
        "minor",
        "Two Include chains produce a cartesian result set. Consider AsSplitQuery().",
    ),
]

#: A query call inside a loop body -- the N+1 shape.
_LOOP = re.compile(r"\b(for|foreach|while)\b")
_QUERY_CALL = re.compile(
    r"\b(ToListAsync|FirstOrDefaultAsync|SingleAsync|AnyAsync|CountAsync|"
    r"ExecuteReader|ExecuteScalar|GetAsync|LoadAsync)\b"
)


class PerformanceReviewPlugin(LLMReviewPlugin):
    """Finds N+1 queries, blocking calls and unbounded reads."""

    name = "performance_review"
    version = "1.0"
    description = "Reviews changed files for performance defects that appear at scale."
    stages = ("review",)
    focus = (
        "performance under production data volumes. Look for queries executed "
        "inside loops (N+1), unbounded result sets, work repeated per item that "
        "could be done once, blocking calls on async paths, and allocations in "
        "hot loops. Judge cost per request at realistic data sizes, not on a "
        "developer's ten-row table."
    )

    @property
    def category(self) -> str:
        return "performance"

    def execute(self, state: AgentState) -> PluginResult:
        pattern_findings = self._scan(state)
        model_result = super().execute(state)
        return PluginResult(
            plugin=self.name,
            findings=pattern_findings + model_result.findings,
            artifacts={**model_result.artifacts, "pattern_findings": len(pattern_findings)},
            ok=model_result.ok,
            error=model_result.error,
        )

    def _scan(self, state: AgentState) -> list[Any]:
        findings: list[Any] = []
        for path in self.changed_files(state)[: self.max_files]:
            content = self.read(path)
            if not content:
                continue
            lines = content.splitlines()
            for number, line in enumerate(lines, start=1):
                for pattern, severity, message in PATTERNS:
                    if pattern.search(line):
                        findings.append(
                            self.finding(
                                message,
                                severity=severity,
                                category="performance",
                                path=path,
                                line=number,
                            )
                        )
                        break
            findings.extend(self._find_n_plus_one(path, lines))
        return findings

    def _find_n_plus_one(self, path: str, lines: list[str], window: int = 8) -> list[Any]:
        """A database call within a few lines below a loop header."""
        findings: list[Any] = []
        for index, line in enumerate(lines):
            if not _LOOP.search(line):
                continue
            for offset in range(1, min(window, len(lines) - index)):
                if _QUERY_CALL.search(lines[index + offset]):
                    findings.append(
                        self.finding(
                            "A database call appears inside a loop (N+1). Load the set "
                            "in one query and join in memory.",
                            severity="major",
                            category="performance",
                            path=path,
                            line=index + offset + 1,
                        )
                    )
                    break
        return findings


__all__ = ["PATTERNS", "PerformanceReviewPlugin"]

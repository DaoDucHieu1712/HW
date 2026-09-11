"""Plugin contract.

A plugin extends the platform without modifying it: it can add tools, register
hooks, and contribute findings at a workflow stage. The four-method lifecycle
the specification calls for is enforced here:

``register`` -> ``validate`` -> ``execute`` (many times) -> ``shutdown``
"""

from __future__ import annotations

import abc
import logging
from dataclasses import dataclass, field
from typing import Any, Sequence

from graph.state import AgentState, ReviewComment
from hooks.events import HookEvent
from hooks.manager import HookHandler, HookManager
from llms.base import LLMClient
from memory.manager import MemoryManager
from tools.base import Tool
from tools.registry import ToolRegistry

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class PluginContext:
    """What a plugin is given when it registers.

    Passing a context rather than the whole container keeps the plugin surface
    explicit: a plugin can reach exactly these things, and nothing else.
    """

    tools: ToolRegistry
    hooks: HookManager
    memory: MemoryManager
    llm: LLMClient
    settings: Any
    options: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class PluginResult:
    """What one ``execute`` produced."""

    plugin: str
    findings: list[ReviewComment] = field(default_factory=list)
    artifacts: dict[str, Any] = field(default_factory=dict)
    ok: bool = True
    error: str | None = None


class Plugin(abc.ABC):
    """Base class for platform plugins."""

    #: Registry key, and the name used in ``enabled_plugins``.
    name: str = ""
    version: str = "1.0"
    description: str = ""
    #: Workflow stages this plugin contributes to, e.g. ``("review",)``.
    stages: tuple[str, ...] = ()

    def __init__(self) -> None:
        self._context: PluginContext | None = None
        self._registered = False

    # -- lifecycle -------------------------------------------------------

    @abc.abstractmethod
    def register(self, context: PluginContext) -> None:
        """Wire the plugin into the platform.

        Called once at startup. Add tools and hooks here; do no work.
        """

    def validate(self) -> list[str]:
        """Return a list of problems; empty means the plugin is usable.

        Called after :meth:`register`. A plugin that reports problems is
        disabled rather than left half-wired.
        """
        return []

    @abc.abstractmethod
    def execute(self, state: AgentState) -> PluginResult:
        """Do the plugin's work for the current state."""

    def shutdown(self) -> None:
        """Release resources. Always called, even when registration failed."""

    # -- helpers for subclasses ------------------------------------------

    @property
    def context(self) -> PluginContext:
        if self._context is None:
            raise RuntimeError(f"plugin {self.name!r} is not registered")
        return self._context

    def _bind(self, context: PluginContext) -> None:
        self._context = context
        self._registered = True

    def add_tool(self, tool: Tool) -> None:
        self.context.tools.register(tool, replace=True)

    def add_hook(self, event: HookEvent, handler: HookHandler, *, priority: int = 100) -> None:
        self.context.hooks.register(
            event, handler, name=f"{self.name}.{event.value}", priority=priority
        )

    def finding(
        self,
        message: str,
        *,
        severity: str = "minor",
        category: str = "correctness",
        path: str = "",
        line: int = 0,
        suggestion: str = "",
    ) -> ReviewComment:
        """Build a finding attributed to this plugin."""
        return ReviewComment(
            path=path,
            line=line,
            severity=severity,
            category=category,
            message=message,
            suggestion=suggestion,
            source=f"plugin:{self.name}",
        )

    def changed_files(self, state: AgentState) -> list[str]:
        """Paths this run touched -- the natural scope for a review plugin."""
        return [
            str(change.get("path", ""))
            for change in (state.get("code_changes") or [])
            if change.get("path")
        ]

    def read(self, path: str) -> str:
        """Read a workspace file through the registered tool, honouring policy."""
        result = self.context.tools.execute("read_file", {"path": path}, source=self.name)
        return result.to_content() if result.ok else ""

    def to_dict(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "version": self.version,
            "description": self.description,
            "stages": list(self.stages),
            "registered": self._registered,
        }

    def __repr__(self) -> str:  # pragma: no cover - debug aid
        return f"<{type(self).__name__} name={self.name!r} stages={self.stages}>"


class LLMReviewPlugin(Plugin):
    """Base for plugins that ask the model to review changed files.

    Subclasses supply a lens (:attr:`focus`) and the schema stays fixed, so
    every review plugin produces findings in the same shape.
    """

    #: What this plugin looks for, injected into the system prompt.
    focus: str = ""
    #: Maximum files examined per run -- review plugins are per-file and add up.
    max_files: int = 8

    findings_schema: dict[str, Any] = {
        "type": "object",
        "properties": {
            "findings": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "path": {"type": "string"},
                        "line": {"type": "integer"},
                        "severity": {
                            "type": "string",
                            "enum": ["blocker", "major", "minor", "nit"],
                        },
                        "message": {"type": "string"},
                        "suggestion": {"type": "string"},
                    },
                    "required": ["severity", "message"],
                },
            }
        },
        "required": ["findings"],
    }

    def register(self, context: PluginContext) -> None:
        self._bind(context)

    def validate(self) -> list[str]:
        problems: list[str] = []
        if not self.focus:
            problems.append("focus is empty; the plugin has no review lens")
        if not self.context.tools.has("read_file"):
            problems.append("the read_file tool is not registered")
        return problems

    def execute(self, state: AgentState) -> PluginResult:
        from llms.base import LLMMessage  # local import keeps the base import graph flat

        paths = self.changed_files(state)[: self.max_files]
        if not paths:
            return PluginResult(plugin=self.name, artifacts={"skipped": "no changed files"})

        sources: list[str] = []
        for path in paths:
            body = self.read(path)
            if body:
                sources.append(f"### {path}\n```\n{body[:12000]}\n```")
        if not sources:
            return PluginResult(plugin=self.name, artifacts={"skipped": "no readable files"})

        system = (
            f"You review code with one lens: {self.focus}\n"
            "Report only defects you can point at, with the file, the line, and the "
            "condition that triggers them. Do not report findings outside your lens, "
            "and do not invent findings to look thorough -- an empty list is a valid "
            "answer for clean code."
        )
        prompt = (
            f"Objective of the change: {state.get('objective', '')}\n\n"
            + "\n\n".join(sources)
        )

        try:
            payload = self.context.llm.complete_structured(
                [LLMMessage.user(prompt)],
                schema=self.findings_schema,
                system=system,
                max_tokens=6000,
            )
        except Exception as exc:  # noqa: BLE001 - a plugin must not break the run
            logger.exception("plugin %s failed", self.name)
            return PluginResult(
                plugin=self.name, ok=False, error=f"{type(exc).__name__}: {exc}"
            )

        findings = [
            self.finding(
                item.get("message", ""),
                severity=str(item.get("severity", "minor")).lower(),
                category=self.category,
                path=item.get("path", ""),
                line=int(item.get("line") or 0),
                suggestion=item.get("suggestion", ""),
            )
            for item in (payload.get("findings") or [])
            if item.get("message")
        ]
        return PluginResult(
            plugin=self.name,
            findings=findings,
            artifacts={f"{self.name}_reviewed": paths},
        )

    @property
    def category(self) -> str:
        """Finding category; subclasses override to match their lens."""
        return "correctness"


def summarise_results(results: Sequence[PluginResult]) -> dict[str, Any]:
    """Fold plugin results into a reportable summary."""
    return {
        "plugins_run": [result.plugin for result in results],
        "findings": sum(len(result.findings) for result in results),
        "failed": [result.plugin for result in results if not result.ok],
    }


__all__ = [
    "LLMReviewPlugin",
    "Plugin",
    "PluginContext",
    "PluginResult",
    "summarise_results",
]

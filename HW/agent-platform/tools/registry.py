"""Registry and hook-aware executor for tools.

The registry is the single place that knows which capabilities exist. Agents ask
for tools by name or tag; nothing constructs a tool ad hoc at call time.
"""

from __future__ import annotations

import logging
from typing import Any, Iterable, Iterator, Mapping, Sequence

from hooks.events import HookEvent
from hooks.manager import HookManager

from .base import Tool, ToolResult

logger = logging.getLogger(__name__)


class ToolRegistry:
    """Name-addressed collection of :class:`Tool` instances."""

    def __init__(self, hooks: HookManager | None = None) -> None:
        self._tools: dict[str, Tool] = {}
        self._hooks = hooks

    # -- registration ----------------------------------------------------

    def register(self, tool: Tool, *, replace: bool = False) -> Tool:
        if not tool.name:
            raise ValueError(f"{type(tool).__name__} has no name")
        if tool.name in self._tools and not replace:
            raise ValueError(f"tool {tool.name!r} is already registered")
        self._tools[tool.name] = tool
        logger.debug("registered tool %s", tool.name)
        return tool

    def register_all(self, tools: Iterable[Tool], *, replace: bool = False) -> None:
        for tool in tools:
            self.register(tool, replace=replace)

    def unregister(self, name: str) -> bool:
        return self._tools.pop(name, None) is not None

    # -- lookup ----------------------------------------------------------

    def get(self, name: str) -> Tool:
        try:
            return self._tools[name]
        except KeyError as exc:
            raise KeyError(
                f"unknown tool {name!r}; registered: {', '.join(sorted(self._tools))}"
            ) from exc

    def has(self, name: str) -> bool:
        return name in self._tools

    def names(self) -> list[str]:
        return sorted(self._tools)

    def by_tag(self, tag: str) -> list[Tool]:
        return [tool for tool in self._tools.values() if tag in tool.tags]

    def subset(self, names: Sequence[str] | None) -> list[Tool]:
        """Resolve an agent's declared tool list.

        ``None`` means "everything"; an empty sequence means "no tools", which
        is a meaningful choice for agents that must only reason.
        """
        if names is None:
            return [self._tools[name] for name in self.names()]
        return [self.get(name) for name in names]

    def schemas(self, names: Sequence[str] | None = None, *, strict: bool = True) -> list[dict[str, Any]]:
        """Tool definitions for the Messages API ``tools`` array."""
        return [tool.to_anthropic_schema(strict=strict) for tool in self.subset(names)]

    def __iter__(self) -> Iterator[Tool]:
        return iter(self._tools.values())

    def __len__(self) -> int:
        return len(self._tools)

    def __contains__(self, name: object) -> bool:
        return name in self._tools

    # -- execution -------------------------------------------------------

    def execute(
        self,
        name: str,
        arguments: Mapping[str, Any] | None = None,
        *,
        source: str = "platform",
    ) -> ToolResult:
        """Run a tool with Before/After hooks around it.

        A ``BeforeToolExecution`` handler may veto the call (policy guard) or
        rewrite ``arguments``; an ``AfterToolExecution`` handler may replace the
        result (redaction, truncation, caching).
        """
        arguments = dict(arguments or {})
        if not self.has(name):
            return ToolResult.failure(name, f"unknown tool {name!r}")

        tool = self.get(name)
        hooks = self._hooks

        if hooks is not None:
            before = hooks.emit(
                HookEvent.BEFORE_TOOL_EXECUTION,
                source,
                tool=name,
                arguments=arguments,
                read_only=tool.read_only,
            )
            if before.cancelled:
                return ToolResult.failure(
                    name, f"blocked by policy: {before.cancel_reason}", vetoed=True
                )
            arguments = dict(before.data.get("arguments", arguments))

        result = tool.run(**arguments)

        if not result.ok and hooks is not None:
            hooks.emit_error(source, RuntimeError(result.error or "tool failed"), tool=name)

        if hooks is not None:
            after = hooks.emit(
                HookEvent.AFTER_TOOL_EXECUTION,
                source,
                tool=name,
                arguments=arguments,
                ok=result.ok,
                duration_ms=result.duration_ms,
            )
            if isinstance(after.result, ToolResult):
                result = after.result
        return result


__all__ = ["ToolRegistry"]

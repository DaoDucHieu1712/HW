"""Tool abstraction shared by agents, MCP proxies and the LLM tool-calling loop.

A tool is a single, independently testable unit: construct it with explicit
collaborators, call :meth:`Tool.run`, assert on the :class:`ToolResult`. No tool
reaches for global state, so every one of them can be exercised in isolation.
"""

from __future__ import annotations

import abc
import json
import logging
import time
from dataclasses import dataclass, field
from typing import Any, Mapping

logger = logging.getLogger(__name__)

#: JSON-Schema fragment describing a tool's parameters.
JSONSchema = dict[str, Any]


class ToolError(RuntimeError):
    """Raised for a failure the caller is expected to handle (bad input, IO)."""


@dataclass(slots=True)
class ToolResult:
    """Outcome of a single tool invocation.

    Failures are returned rather than raised so the agent loop can feed the
    error back to the model as a ``tool_result`` with ``is_error`` set, which is
    usually more useful than aborting the run.
    """

    tool: str
    ok: bool
    data: Any = None
    error: str | None = None
    metadata: dict[str, Any] = field(default_factory=dict)
    duration_ms: float = 0.0

    @classmethod
    def success(cls, tool: str, data: Any, **metadata: Any) -> "ToolResult":
        return cls(tool=tool, ok=True, data=data, metadata=metadata)

    @classmethod
    def failure(cls, tool: str, error: str, **metadata: Any) -> "ToolResult":
        return cls(tool=tool, ok=False, error=error, metadata=metadata)

    def to_content(self) -> str:
        """Render the result as the string body of an API ``tool_result`` block."""
        if not self.ok:
            return f"ERROR: {self.error}"
        if isinstance(self.data, str):
            return self.data
        try:
            return json.dumps(self.data, indent=2, default=str)
        except (TypeError, ValueError):  # pragma: no cover - defensive
            return str(self.data)

    def to_dict(self) -> dict[str, Any]:
        return {
            "tool": self.tool,
            "ok": self.ok,
            "data": self.data,
            "error": self.error,
            "metadata": self.metadata,
            "duration_ms": round(self.duration_ms, 2),
        }


class Tool(abc.ABC):
    """Base class for every callable capability exposed to an agent.

    Subclasses declare :attr:`name`, :attr:`description` and
    :attr:`input_schema`, then implement :meth:`_execute`. Validation, timing
    and error translation are handled here so subclasses stay small.
    """

    #: Stable identifier used in configuration and in the model's tool call.
    name: str = ""
    #: Shown to the model. This is prompt surface -- write it for the model.
    description: str = ""
    #: JSON Schema for the arguments. ``additionalProperties`` is forced off so
    #: the tool can be declared with ``strict: true``.
    input_schema: JSONSchema = {"type": "object", "properties": {}, "required": []}
    #: Tools that only read are safe to run in parallel and in read-only mode.
    read_only: bool = True
    #: Tags let an agent select a family of tools without naming each one.
    tags: tuple[str, ...] = ()

    # -- public API ------------------------------------------------------

    def run(self, **arguments: Any) -> ToolResult:
        """Validate, execute and time the tool. Never raises for tool-level errors."""
        started = time.perf_counter()
        try:
            cleaned = self.validate(arguments)
            data = self._execute(**cleaned)
            result = data if isinstance(data, ToolResult) else ToolResult.success(self.name, data)
        except ToolError as exc:
            result = ToolResult.failure(self.name, str(exc))
        except Exception as exc:  # noqa: BLE001 - surface as data, not a crash
            logger.exception("tool %s raised", self.name)
            result = ToolResult.failure(self.name, f"{type(exc).__name__}: {exc}")
        result.duration_ms = (time.perf_counter() - started) * 1000.0
        return result

    def validate(self, arguments: Mapping[str, Any]) -> dict[str, Any]:
        """Check required keys and drop unknown ones.

        Deliberately shallow: the API already enforces the full schema when the
        tool is declared ``strict``. This guards direct/programmatic calls.
        """
        schema = self.input_schema or {}
        properties: Mapping[str, Any] = schema.get("properties", {})
        required: list[str] = list(schema.get("required", []))

        missing = [key for key in required if arguments.get(key) in (None, "")]
        if missing:
            raise ToolError(f"missing required argument(s): {', '.join(missing)}")

        unknown = set(arguments) - set(properties)
        if unknown:
            logger.debug("tool %s ignoring unknown arguments: %s", self.name, sorted(unknown))
        return {key: value for key, value in arguments.items() if key in properties}

    @abc.abstractmethod
    def _execute(self, **arguments: Any) -> Any:
        """Do the work. Return raw data or a fully-formed :class:`ToolResult`."""

    # -- interop ---------------------------------------------------------

    def to_anthropic_schema(self, *, strict: bool = True) -> dict[str, Any]:
        """Render the tool definition for the Messages API ``tools`` array."""
        schema = dict(self.input_schema or {"type": "object", "properties": {}})
        schema.setdefault("type", "object")
        schema.setdefault("properties", {})
        schema.setdefault("required", [])
        definition: dict[str, Any] = {
            "name": self.name,
            "description": self.description.strip(),
            "input_schema": schema,
        }
        if strict:
            # `strict` guarantees the arguments validate; it requires the schema
            # to close itself off and list its required keys.
            schema["additionalProperties"] = False
            definition["strict"] = True
        return definition

    def __repr__(self) -> str:  # pragma: no cover - debug aid
        return f"<{type(self).__name__} name={self.name!r} read_only={self.read_only}>"


class FunctionTool(Tool):
    """Adapter that turns a plain callable into a :class:`Tool`.

    Useful for one-off capabilities and for tests that need a stub tool without
    declaring a class.
    """

    def __init__(
        self,
        name: str,
        description: str,
        input_schema: JSONSchema,
        func: Any,
        *,
        read_only: bool = True,
        tags: tuple[str, ...] = (),
    ) -> None:
        self.name = name
        self.description = description
        self.input_schema = input_schema
        self.read_only = read_only
        self.tags = tags
        self._func = func

    def _execute(self, **arguments: Any) -> Any:
        return self._func(**arguments)


__all__ = ["FunctionTool", "JSONSchema", "Tool", "ToolError", "ToolResult"]

"""MCP server wrappers.

Each wrapper is the platform's own description of one MCP server: its config,
the tools it is expected to expose, a health check, and registration into the
tool registry. Transport is injected, so a server can be described, documented
and health-checked without a live connection -- which is what makes the registry
honest in an environment where only some servers are configured.
"""

from __future__ import annotations

import abc
import logging
import os
import re
import time
from dataclasses import dataclass, field
from typing import Any, Mapping, Sequence

from configs.settings import MCPServerSettings
from tools.base import Tool, ToolError, ToolResult

logger = logging.getLogger(__name__)

_ENV_REF = re.compile(r"\$\{(\w+)\}")


class MCPUnavailable(RuntimeError):
    """The server is not configured, not reachable, or not enabled."""


@dataclass(slots=True)
class MCPToolSpec:
    """One tool an MCP server exposes."""

    name: str
    description: str
    input_schema: dict[str, Any] = field(
        default_factory=lambda: {"type": "object", "properties": {}, "required": []}
    )
    read_only: bool = True

    def to_dict(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "description": self.description,
            "read_only": self.read_only,
            "input_schema": self.input_schema,
        }


@dataclass(slots=True)
class HealthStatus:
    """Result of a health check."""

    server: str
    healthy: bool
    detail: str = ""
    latency_ms: float = 0.0
    tools: int = 0

    def to_dict(self) -> dict[str, Any]:
        return {
            "server": self.server,
            "healthy": self.healthy,
            "detail": self.detail,
            "latency_ms": round(self.latency_ms, 2),
            "tools": self.tools,
        }


class MCPTransport(abc.ABC):
    """How the platform talks to an MCP server."""

    @abc.abstractmethod
    def connect(self) -> None:
        """Establish the connection. Idempotent."""

    @abc.abstractmethod
    def list_tools(self) -> list[MCPToolSpec]:
        """Tools the live server reports."""

    @abc.abstractmethod
    def call(self, tool: str, arguments: Mapping[str, Any]) -> Any:
        """Invoke a tool and return its result."""

    @abc.abstractmethod
    def ping(self) -> bool:
        """Cheap liveness check."""

    def close(self) -> None:
        """Release the connection."""


class NullTransport(MCPTransport):
    """Stand-in for a server that is disabled or not configured.

    Every call raises :class:`MCPUnavailable` with the reason. That is
    deliberately louder than silently returning nothing: a workflow that depends
    on an unconfigured server should say so, not produce an empty answer.
    """

    def __init__(self, reason: str) -> None:
        self.reason = reason

    def connect(self) -> None:
        raise MCPUnavailable(self.reason)

    def list_tools(self) -> list[MCPToolSpec]:
        return []

    def call(self, tool: str, arguments: Mapping[str, Any]) -> Any:
        raise MCPUnavailable(self.reason)

    def ping(self) -> bool:
        return False


class BaseMCPServer(abc.ABC):
    """Base wrapper for one MCP server."""

    #: Registry key, matching the entry in ``configs/mcps.yaml``.
    name: str = ""
    #: What this server gives the platform access to.
    description: str = ""

    def __init__(
        self,
        settings: MCPServerSettings,
        *,
        transport: MCPTransport | None = None,
    ) -> None:
        self.settings = settings
        self._transport = transport
        self._connected = False

    # -- description -----------------------------------------------------

    @abc.abstractmethod
    def declared_tools(self) -> list[MCPToolSpec]:
        """Tools this server is expected to expose.

        Declared statically so the platform can document and validate the server
        without connecting -- and so a typo in a tool name is caught at startup.
        """

    # -- lifecycle -------------------------------------------------------

    @property
    def enabled(self) -> bool:
        return self.settings.enabled

    @property
    def transport(self) -> MCPTransport:
        if self._transport is None:
            self._transport = self._build_transport()
        return self._transport

    def _build_transport(self) -> MCPTransport:
        if not self.settings.enabled:
            return NullTransport(f"MCP server {self.name!r} is disabled in configuration")
        from .transports import build_transport  # local import avoids a cycle

        return build_transport(self.settings)

    def connect(self) -> None:
        if self._connected:
            return
        self.transport.connect()
        self._connected = True
        logger.info("connected to MCP server %s", self.name)

    def shutdown(self) -> None:
        if self._transport is not None:
            self._transport.close()
        self._connected = False

    # -- use -------------------------------------------------------------

    def list_tools(self) -> list[MCPToolSpec]:
        """Live tool list when connected, declared list otherwise."""
        if not self.enabled:
            return self.declared_tools()
        try:
            self.connect()
            live = self.transport.list_tools()
            return live or self.declared_tools()
        except MCPUnavailable:
            return self.declared_tools()
        except Exception as exc:  # noqa: BLE001 - discovery must not break startup
            logger.warning("could not list tools for %s: %s", self.name, exc)
            return self.declared_tools()

    def call_tool(self, tool: str, arguments: Mapping[str, Any] | None = None) -> Any:
        self.connect()
        return self.transport.call(tool, dict(arguments or {}))

    def health_check(self) -> HealthStatus:
        """Report whether this server can actually be used right now."""
        if not self.settings.enabled:
            return HealthStatus(self.name, False, "disabled in configuration")

        missing = self.missing_environment()
        if missing:
            return HealthStatus(
                self.name, False, f"missing environment variable(s): {', '.join(missing)}"
            )

        started = time.perf_counter()
        try:
            self.connect()
            alive = self.transport.ping()
            tools = len(self.transport.list_tools())
        except MCPUnavailable as exc:
            return HealthStatus(self.name, False, str(exc))
        except Exception as exc:  # noqa: BLE001 - report, do not raise
            return HealthStatus(self.name, False, f"{type(exc).__name__}: {exc}")

        elapsed = (time.perf_counter() - started) * 1000.0
        return HealthStatus(
            self.name,
            alive,
            "ok" if alive else "ping failed",
            latency_ms=elapsed,
            tools=tools,
        )

    def missing_environment(self) -> list[str]:
        """Environment variables referenced by the config but not set."""
        missing: list[str] = []
        for value in self.settings.env.values():
            for name in _ENV_REF.findall(str(value)):
                if not os.getenv(name):
                    missing.append(name)
        return sorted(set(missing))

    # -- registration ----------------------------------------------------

    def as_tools(self, *, prefix: bool = True) -> list[Tool]:
        """Adapt this server's tools into platform :class:`Tool` objects."""
        return [
            MCPToolProxy(self, spec, prefix=prefix) for spec in self.list_tools()
        ]

    def to_dict(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "description": self.description,
            "enabled": self.settings.enabled,
            "transport": self.settings.transport,
            "tools": [spec.name for spec in self.declared_tools()],
        }

    def __repr__(self) -> str:  # pragma: no cover - debug aid
        return f"<{type(self).__name__} name={self.name!r} enabled={self.settings.enabled}>"


class MCPToolProxy(Tool):
    """Presents one MCP tool through the platform's :class:`Tool` interface.

    Agents cannot tell an MCP-backed tool from a native one, which is the point:
    the tool-calling loop, the policy hooks and the audit trail apply uniformly.
    """

    def __init__(self, server: BaseMCPServer, spec: MCPToolSpec, *, prefix: bool = True) -> None:
        self.server = server
        self.spec = spec
        self.name = f"{server.name}__{spec.name}" if prefix else spec.name
        self.description = f"[{server.name} MCP] {spec.description}"
        self.input_schema = spec.input_schema
        self.read_only = spec.read_only
        self.tags = ("mcp", server.name)

    def _execute(self, **arguments: Any) -> ToolResult:
        try:
            data = self.server.call_tool(self.spec.name, arguments)
        except MCPUnavailable as exc:
            raise ToolError(str(exc)) from exc
        except Exception as exc:  # noqa: BLE001 - surfaced to the model as an error
            raise ToolError(f"{self.server.name}: {type(exc).__name__}: {exc}") from exc
        return ToolResult.success(self.name, data, server=self.server.name)


def text_schema(**properties: str) -> dict[str, Any]:
    """Shorthand for a flat object schema of string properties."""
    return {
        "type": "object",
        "properties": {
            name: {"type": "string", "description": description}
            for name, description in properties.items()
        },
        "required": list(properties),
    }


def schema(properties: Mapping[str, Any], required: Sequence[str] = ()) -> dict[str, Any]:
    """Build a JSON Schema object from a property mapping."""
    return {"type": "object", "properties": dict(properties), "required": list(required)}


__all__ = [
    "BaseMCPServer",
    "HealthStatus",
    "MCPToolProxy",
    "MCPToolSpec",
    "MCPTransport",
    "MCPUnavailable",
    "NullTransport",
    "schema",
    "text_schema",
]

"""MCP transports: stdio (local process) and streamable HTTP.

The MCP Python SDK is asynchronous and session-oriented; the platform's agent
loop is synchronous. Rather than colour the whole codebase async for one
integration, the stdio transport owns a dedicated event loop on its own thread
and marshals calls onto it. That keeps the session alive across calls -- an MCP
session is stateful, so opening one per call would be both slow and wrong.
"""

from __future__ import annotations

import asyncio
import logging
import os
import re
import threading
from concurrent.futures import Future
from typing import Any, Mapping

from configs.settings import MCPServerSettings
from integrations.http import HttpClient, HttpError

from .base import MCPToolSpec, MCPTransport, MCPUnavailable

logger = logging.getLogger(__name__)

_ENV_REF = re.compile(r"\$\{(\w+)\}")


def expand_env(value: str) -> str:
    """Replace ``${VAR}`` with the environment value, or empty when unset."""
    return _ENV_REF.sub(lambda match: os.getenv(match.group(1), ""), value)


def build_transport(settings: MCPServerSettings) -> MCPTransport:
    """Pick the transport for a server configuration."""
    kind = (settings.transport or "stdio").lower()
    if kind == "stdio":
        return StdioMCPTransport(settings)
    if kind in {"http", "sse", "streamable-http"}:
        return HttpMCPTransport(settings)
    from .base import NullTransport

    return NullTransport(f"unsupported MCP transport {settings.transport!r}")


class _LoopThread:
    """A background event loop that outlives individual calls."""

    def __init__(self, name: str) -> None:
        self._loop = asyncio.new_event_loop()
        self._thread = threading.Thread(
            target=self._run, name=f"mcp-{name}", daemon=True
        )
        self._thread.start()

    def _run(self) -> None:
        asyncio.set_event_loop(self._loop)
        self._loop.run_forever()

    def submit(self, coroutine: Any, timeout: float) -> Any:
        future: Future = asyncio.run_coroutine_threadsafe(coroutine, self._loop)
        return future.result(timeout=timeout)

    def stop(self) -> None:
        self._loop.call_soon_threadsafe(self._loop.stop)
        self._thread.join(timeout=5)


class StdioMCPTransport(MCPTransport):
    """Talks to an MCP server started as a child process over stdio."""

    def __init__(self, settings: MCPServerSettings) -> None:
        self.settings = settings
        self._loop: _LoopThread | None = None
        self._session: Any | None = None
        self._exit_stack: Any | None = None

    # -- lifecycle -------------------------------------------------------

    def connect(self) -> None:
        if self._session is not None:
            return
        if not self.settings.command:
            raise MCPUnavailable(f"MCP server {self.settings.name!r} has no command configured")

        try:
            from mcp import ClientSession, StdioServerParameters
            from mcp.client.stdio import stdio_client
        except ImportError as exc:
            raise MCPUnavailable(
                "the `mcp` package is not installed; run `pip install mcp` to use stdio servers"
            ) from exc

        from contextlib import AsyncExitStack

        parameters = StdioServerParameters(
            command=self.settings.command,
            args=[expand_env(arg) for arg in self.settings.args],
            env={**os.environ, **{k: expand_env(v) for k, v in self.settings.env.items()}},
        )

        async def open_session() -> tuple[Any, Any]:
            stack = AsyncExitStack()
            read, write = await stack.enter_async_context(stdio_client(parameters))
            session = await stack.enter_async_context(ClientSession(read, write))
            await session.initialize()
            return session, stack

        self._loop = _LoopThread(self.settings.name)
        try:
            self._session, self._exit_stack = self._loop.submit(
                open_session(), self.settings.timeout_seconds
            )
        except Exception as exc:
            self._loop.stop()
            self._loop = None
            raise MCPUnavailable(
                f"could not start MCP server {self.settings.name!r}: {exc}"
            ) from exc

    def close(self) -> None:
        if self._loop is not None and self._exit_stack is not None:
            try:
                self._loop.submit(self._exit_stack.aclose(), 10)
            except Exception:  # noqa: BLE001 - shutdown is best effort
                logger.debug("MCP %s did not close cleanly", self.settings.name)
        if self._loop is not None:
            self._loop.stop()
        self._loop = None
        self._session = None
        self._exit_stack = None

    # -- calls -----------------------------------------------------------

    def _require(self) -> tuple[Any, _LoopThread]:
        self.connect()
        if self._session is None or self._loop is None:  # pragma: no cover - defensive
            raise MCPUnavailable(f"MCP server {self.settings.name!r} is not connected")
        return self._session, self._loop

    def list_tools(self) -> list[MCPToolSpec]:
        session, loop = self._require()
        response = loop.submit(session.list_tools(), self.settings.timeout_seconds)
        return [
            MCPToolSpec(
                name=tool.name,
                description=tool.description or "",
                input_schema=dict(getattr(tool, "inputSchema", None) or {"type": "object", "properties": {}}),
            )
            for tool in getattr(response, "tools", [])
        ]

    def call(self, tool: str, arguments: Mapping[str, Any]) -> Any:
        session, loop = self._require()
        response = loop.submit(
            session.call_tool(tool, dict(arguments)), self.settings.timeout_seconds
        )
        return _unwrap(response)

    def ping(self) -> bool:
        try:
            self.list_tools()
            return True
        except Exception:  # noqa: BLE001 - a failed ping is a health answer
            return False


class HttpMCPTransport(MCPTransport):
    """Talks to a remote MCP server over JSON-RPC on HTTP."""

    def __init__(self, settings: MCPServerSettings) -> None:
        self.settings = settings
        self._client: HttpClient | None = None
        self._request_id = 0

    def connect(self) -> None:
        if self._client is not None:
            return
        if not self.settings.url:
            raise MCPUnavailable(f"MCP server {self.settings.name!r} has no url configured")
        client = HttpClient(base_url=self.settings.url, timeout=self.settings.timeout_seconds)
        client.headers["Accept"] = "application/json, text/event-stream"
        for key, value in self.settings.env.items():
            if key.lower().endswith(("token", "key")):
                client.with_bearer(expand_env(value))
        self._client = client

    def _rpc(self, method: str, params: Mapping[str, Any] | None = None) -> Any:
        self.connect()
        assert self._client is not None
        self._request_id += 1
        try:
            response = self._client.post(
                "",
                body={
                    "jsonrpc": "2.0",
                    "id": self._request_id,
                    "method": method,
                    "params": dict(params or {}),
                },
            )
        except HttpError as exc:
            raise MCPUnavailable(f"{self.settings.name}: {exc}") from exc

        if isinstance(response, dict) and response.get("error"):
            error = response["error"]
            raise MCPUnavailable(f"{self.settings.name}: {error.get('message', error)}")
        return (response or {}).get("result") if isinstance(response, dict) else response

    def list_tools(self) -> list[MCPToolSpec]:
        result = self._rpc("tools/list") or {}
        return [
            MCPToolSpec(
                name=tool.get("name", ""),
                description=tool.get("description", ""),
                input_schema=tool.get("inputSchema") or {"type": "object", "properties": {}},
            )
            for tool in result.get("tools", [])
            if tool.get("name")
        ]

    def call(self, tool: str, arguments: Mapping[str, Any]) -> Any:
        return _unwrap(self._rpc("tools/call", {"name": tool, "arguments": dict(arguments)}))

    def ping(self) -> bool:
        try:
            self._rpc("tools/list")
            return True
        except Exception:  # noqa: BLE001
            return False

    def close(self) -> None:
        self._client = None


def _unwrap(response: Any) -> Any:
    """Flatten an MCP tool result into plain data.

    MCP returns a list of content blocks; text blocks are the common case, and
    an agent is better served by the text than by the envelope.
    """
    content = getattr(response, "content", None)
    if content is None and isinstance(response, dict):
        content = response.get("content")
    if content is None:
        return response

    parts: list[str] = []
    for block in content:
        text = getattr(block, "text", None)
        if text is None and isinstance(block, dict):
            text = block.get("text")
        if text is not None:
            parts.append(str(text))
    return "\n".join(parts) if parts else content


__all__ = [
    "HttpMCPTransport",
    "StdioMCPTransport",
    "build_transport",
    "expand_env",
]

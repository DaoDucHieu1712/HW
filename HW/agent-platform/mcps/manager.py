"""MCP registration and lifecycle.

The manager builds the wrappers from configuration, registers the tools of the
*enabled* ones into the shared tool registry, and reports health. Disabled
servers stay described but unregistered: an agent should not be offered a tool
that cannot run.
"""

from __future__ import annotations

import logging
from typing import Any, Iterator, Mapping

from configs.settings import MCPServerSettings, PlatformSettings
from tools.registry import ToolRegistry

from .base import BaseMCPServer, HealthStatus
from .servers import SERVER_TYPES

logger = logging.getLogger(__name__)


class MCPManager:
    """Owns every configured MCP server."""

    def __init__(self) -> None:
        self._servers: dict[str, BaseMCPServer] = {}

    # -- construction ----------------------------------------------------

    @classmethod
    def from_settings(cls, settings: PlatformSettings) -> "MCPManager":
        manager = cls()
        for name, server_settings in settings.mcps.items():
            server_type = SERVER_TYPES.get(name)
            if server_type is None:
                logger.warning("no wrapper for MCP server %r; skipping", name)
                continue
            manager.add(server_type(server_settings))
        return manager

    def add(self, server: BaseMCPServer) -> BaseMCPServer:
        self._servers[server.name] = server
        return server

    # -- access ----------------------------------------------------------

    def get(self, name: str) -> BaseMCPServer:
        try:
            return self._servers[name]
        except KeyError as exc:
            raise KeyError(
                f"unknown MCP server {name!r}; configured: {', '.join(sorted(self._servers))}"
            ) from exc

    def names(self) -> list[str]:
        return sorted(self._servers)

    def enabled(self) -> list[BaseMCPServer]:
        return [server for server in self._servers.values() if server.enabled]

    def describe(self) -> list[dict[str, Any]]:
        return [server.to_dict() for server in sorted(self._servers.values(), key=lambda s: s.name)]

    # -- registration ----------------------------------------------------

    def register_tools(self, registry: ToolRegistry, *, prefix: bool = True) -> int:
        """Register the tools of every enabled, reachable server.

        Returns the number of tools added. A server that cannot be reached is
        logged and skipped -- startup does not depend on an external process.
        """
        added = 0
        for server in self.enabled():
            try:
                tools = server.as_tools(prefix=prefix)
            except Exception as exc:  # noqa: BLE001 - one bad server must not stop startup
                logger.error("could not register tools for MCP %s: %s", server.name, exc)
                continue
            for tool in tools:
                registry.register(tool, replace=True)
                added += 1
            logger.info("MCP %s contributed %d tool(s)", server.name, len(tools))
        return added

    # -- operations ------------------------------------------------------

    def health_check(self, name: str | None = None) -> list[HealthStatus]:
        """Check one server, or all of them."""
        targets = [self.get(name)] if name else list(self._servers.values())
        return [server.health_check() for server in sorted(targets, key=lambda s: s.name)]

    def shutdown(self) -> None:
        for server in self._servers.values():
            try:
                server.shutdown()
            except Exception:  # noqa: BLE001 - pragma: no cover
                logger.exception("MCP %s failed during shutdown", server.name)

    def __iter__(self) -> Iterator[BaseMCPServer]:
        return iter(self._servers.values())

    def __len__(self) -> int:
        return len(self._servers)


def build_mcp_manager(
    settings: PlatformSettings,
    registry: ToolRegistry | None = None,
    *,
    overrides: Mapping[str, MCPServerSettings] | None = None,
) -> MCPManager:
    """Build the manager and, when a registry is given, register its tools."""
    if overrides:
        settings.mcps = {**settings.mcps, **dict(overrides)}
    manager = MCPManager.from_settings(settings)
    if registry is not None:
        manager.register_tools(registry)
    return manager


__all__ = ["MCPManager", "build_mcp_manager"]

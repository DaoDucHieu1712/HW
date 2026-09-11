"""MCP (Model Context Protocol) server wrappers and registration."""

from .base import (
    BaseMCPServer,
    HealthStatus,
    MCPToolProxy,
    MCPToolSpec,
    MCPTransport,
    MCPUnavailable,
    NullTransport,
)
from .manager import MCPManager, build_mcp_manager
from .servers import (
    SERVER_TYPES,
    DockerMCPServer,
    FilesystemMCPServer,
    GitHubMCPServer,
    JiraMCPServer,
    MongoDBMCPServer,
    RedisMCPServer,
    SeqMCPServer,
    SqlServerMCPServer,
)
from .transports import HttpMCPTransport, StdioMCPTransport, build_transport

__all__ = [
    "BaseMCPServer",
    "DockerMCPServer",
    "FilesystemMCPServer",
    "GitHubMCPServer",
    "HealthStatus",
    "HttpMCPTransport",
    "JiraMCPServer",
    "MCPManager",
    "MCPToolProxy",
    "MCPToolSpec",
    "MCPTransport",
    "MCPUnavailable",
    "MongoDBMCPServer",
    "NullTransport",
    "RedisMCPServer",
    "SERVER_TYPES",
    "SeqMCPServer",
    "SqlServerMCPServer",
    "StdioMCPTransport",
    "build_mcp_manager",
    "build_transport",
]

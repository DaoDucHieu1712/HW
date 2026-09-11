"""The eight MCP server wrappers the platform ships with.

Each declares the tools it is expected to expose. The declaration is what makes
``cli.py mcp health`` and the startup validation useful: a server that is
enabled but exposes a different tool set is a configuration error worth
surfacing, not a mystery at call time.
"""

from __future__ import annotations

from .base import BaseMCPServer, MCPToolSpec, schema


class FilesystemMCPServer(BaseMCPServer):
    name = "filesystem"
    description = "Sandboxed file access outside the platform's own workspace root."

    def declared_tools(self) -> list[MCPToolSpec]:
        return [
            MCPToolSpec(
                "read_file",
                "Read a file from the server's allowed directories.",
                schema({"path": {"type": "string"}}, ["path"]),
            ),
            MCPToolSpec(
                "write_file",
                "Write a file within the server's allowed directories.",
                schema(
                    {"path": {"type": "string"}, "content": {"type": "string"}},
                    ["path", "content"],
                ),
                read_only=False,
            ),
            MCPToolSpec(
                "list_directory",
                "List the entries of a directory.",
                schema({"path": {"type": "string"}}, ["path"]),
            ),
            MCPToolSpec(
                "search_files",
                "Find files matching a glob pattern.",
                schema(
                    {"path": {"type": "string"}, "pattern": {"type": "string"}},
                    ["path", "pattern"],
                ),
            ),
        ]


class GitHubMCPServer(BaseMCPServer):
    name = "github"
    description = "Repository, issue and pull request access on GitHub."

    def declared_tools(self) -> list[MCPToolSpec]:
        return [
            MCPToolSpec(
                "search_code",
                "Search code across the repository.",
                schema({"q": {"type": "string"}}, ["q"]),
            ),
            MCPToolSpec(
                "get_file_contents",
                "Read a file at a ref.",
                schema(
                    {
                        "owner": {"type": "string"},
                        "repo": {"type": "string"},
                        "path": {"type": "string"},
                        "ref": {"type": "string"},
                    },
                    ["owner", "repo", "path"],
                ),
            ),
            MCPToolSpec(
                "list_pull_requests",
                "List pull requests, optionally filtered by state.",
                schema(
                    {
                        "owner": {"type": "string"},
                        "repo": {"type": "string"},
                        "state": {"type": "string"},
                    },
                    ["owner", "repo"],
                ),
            ),
            MCPToolSpec(
                "create_pull_request",
                "Open a pull request.",
                schema(
                    {
                        "owner": {"type": "string"},
                        "repo": {"type": "string"},
                        "title": {"type": "string"},
                        "head": {"type": "string"},
                        "base": {"type": "string"},
                        "body": {"type": "string"},
                    },
                    ["owner", "repo", "title", "head", "base"],
                ),
                read_only=False,
            ),
        ]


class JiraMCPServer(BaseMCPServer):
    name = "jira"
    description = "Jira issue lookup and JQL search."

    def declared_tools(self) -> list[MCPToolSpec]:
        return [
            MCPToolSpec(
                "get_issue",
                "Fetch one issue by key.",
                schema({"issue_key": {"type": "string"}}, ["issue_key"]),
            ),
            MCPToolSpec(
                "search_issues",
                "Search issues with JQL.",
                schema(
                    {"jql": {"type": "string"}, "limit": {"type": "integer"}},
                    ["jql"],
                ),
            ),
            MCPToolSpec(
                "add_comment",
                "Add a comment to an issue.",
                schema(
                    {"issue_key": {"type": "string"}, "body": {"type": "string"}},
                    ["issue_key", "body"],
                ),
                read_only=False,
            ),
        ]


class SqlServerMCPServer(BaseMCPServer):
    name = "sqlserver"
    description = "Read-only SQL Server access for diagnostics and schema inspection."

    def declared_tools(self) -> list[MCPToolSpec]:
        return [
            MCPToolSpec(
                "query",
                "Run a read-only SQL query.",
                schema(
                    {"sql": {"type": "string"}, "parameters": {"type": "array"}},
                    ["sql"],
                ),
            ),
            MCPToolSpec(
                "list_tables",
                "List tables in a schema.",
                schema({"schema": {"type": "string"}}),
            ),
            MCPToolSpec(
                "describe_table",
                "Return the columns, types and keys of a table.",
                schema({"table": {"type": "string"}}, ["table"]),
            ),
        ]


class MongoDBMCPServer(BaseMCPServer):
    name = "mongodb"
    description = "MongoDB collection queries and index inspection."

    def declared_tools(self) -> list[MCPToolSpec]:
        return [
            MCPToolSpec(
                "find",
                "Query a collection with a filter document.",
                schema(
                    {
                        "collection": {"type": "string"},
                        "filter": {"type": "object"},
                        "limit": {"type": "integer"},
                    },
                    ["collection"],
                ),
            ),
            MCPToolSpec(
                "aggregate",
                "Run an aggregation pipeline.",
                schema(
                    {"collection": {"type": "string"}, "pipeline": {"type": "array"}},
                    ["collection", "pipeline"],
                ),
            ),
            MCPToolSpec(
                "list_collections",
                "List the collections in the database.",
                schema({}),
            ),
        ]


class RedisMCPServer(BaseMCPServer):
    name = "redis"
    description = "Redis key inspection for cache and session diagnostics."

    def declared_tools(self) -> list[MCPToolSpec]:
        return [
            MCPToolSpec(
                "get",
                "Read the value at a key.",
                schema({"key": {"type": "string"}}, ["key"]),
            ),
            MCPToolSpec(
                "keys",
                "List keys matching a pattern. Use a narrow pattern on a large instance.",
                schema({"pattern": {"type": "string"}}, ["pattern"]),
            ),
            MCPToolSpec(
                "ttl",
                "Time to live of a key, in seconds.",
                schema({"key": {"type": "string"}}, ["key"]),
            ),
            MCPToolSpec("info", "Server statistics.", schema({})),
        ]


class SeqMCPServer(BaseMCPServer):
    name = "seq"
    description = "Structured log search in Seq."

    def declared_tools(self) -> list[MCPToolSpec]:
        return [
            MCPToolSpec(
                "search_events",
                "Search log events with a Seq filter expression.",
                schema(
                    {
                        "filter": {"type": "string"},
                        "count": {"type": "integer"},
                        "from_utc": {"type": "string"},
                    },
                ),
            ),
            MCPToolSpec(
                "get_event",
                "Fetch one event by id.",
                schema({"event_id": {"type": "string"}}, ["event_id"]),
            ),
            MCPToolSpec(
                "list_signals",
                "List the saved signals available for filtering.",
                schema({}),
            ),
        ]


class DockerMCPServer(BaseMCPServer):
    name = "docker"
    description = "Container inspection for reproducing and diagnosing runtime issues."

    def declared_tools(self) -> list[MCPToolSpec]:
        return [
            MCPToolSpec(
                "list_containers",
                "List containers and their status.",
                schema({"all": {"type": "boolean"}}),
            ),
            MCPToolSpec(
                "container_logs",
                "Read a container's logs.",
                schema(
                    {"container": {"type": "string"}, "tail": {"type": "integer"}},
                    ["container"],
                ),
            ),
            MCPToolSpec(
                "inspect_container",
                "Full inspection output for a container.",
                schema({"container": {"type": "string"}}, ["container"]),
            ),
            MCPToolSpec(
                "compose_ps",
                "Status of the services in a compose project.",
                schema({"project": {"type": "string"}}),
            ),
        ]


#: Registered wrappers, keyed by the name used in ``configs/mcps.yaml``.
SERVER_TYPES: dict[str, type[BaseMCPServer]] = {
    "filesystem": FilesystemMCPServer,
    "github": GitHubMCPServer,
    "jira": JiraMCPServer,
    "sqlserver": SqlServerMCPServer,
    "mongodb": MongoDBMCPServer,
    "redis": RedisMCPServer,
    "seq": SeqMCPServer,
    "docker": DockerMCPServer,
}

__all__ = [
    "DockerMCPServer",
    "FilesystemMCPServer",
    "GitHubMCPServer",
    "JiraMCPServer",
    "MongoDBMCPServer",
    "RedisMCPServer",
    "SERVER_TYPES",
    "SeqMCPServer",
    "SqlServerMCPServer",
]

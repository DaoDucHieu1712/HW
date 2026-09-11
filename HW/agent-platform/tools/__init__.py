"""Tool layer: the capabilities agents can call.

:func:`build_default_tools` is the composition root for tools -- it is the only
place that knows how a tool is constructed, so agents and workflows can depend
on names alone.
"""

from __future__ import annotations

import logging

from configs.settings import PlatformSettings
from integrations.github_client import GitHubClient
from integrations.jira_client import JiraClient
from integrations.seq_client import SeqClient
from llms.base import LLMClient

from .base import FunctionTool, JSONSchema, Tool, ToolError, ToolResult
from .code_tools import BuildSolutionTool, GenerateUnitTestTool, RunTestsTool
from .db_tools import QueryDatabaseTool, QueryRunner, assert_read_only
from .filesystem_tools import ReadFileTool, SearchCodeTool, WriteFileTool
from .jira_tools import QueryJiraTool
from .log_tools import AnalyzeLogsTool
from .registry import ToolRegistry
from .vcs_tools import CreatePullRequestTool
from .workspace import CommandOutcome, Workspace, WorkspaceError

logger = logging.getLogger(__name__)


def _unconfigured_runner(sql: str, parameters: list | None = None) -> list[dict]:
    """Placeholder query runner used until a real driver is injected."""
    raise RuntimeError(
        "no database runner configured; inject one via build_default_tools(query_runner=...)"
    )


def build_default_tools(
    settings: PlatformSettings,
    *,
    llm: LLMClient,
    workspace: Workspace | None = None,
    jira: JiraClient | None = None,
    github: GitHubClient | None = None,
    seq: SeqClient | None = None,
    query_runner: QueryRunner | None = None,
    hooks: object | None = None,
) -> ToolRegistry:
    """Construct and register the platform's standard tool set.

    Tools whose backing system is unconfigured are still registered: calling one
    returns a clear "not configured" failure, which is more useful to the model
    than the tool silently not existing.
    """
    workspace = workspace or Workspace(settings.workspace)
    jira = jira or JiraClient(settings.integrations)
    github = github or GitHubClient(settings.integrations)
    seq = seq or SeqClient(settings.integrations)

    registry = ToolRegistry(hooks=hooks)  # type: ignore[arg-type]
    registry.register_all(
        [
            ReadFileTool(workspace),
            WriteFileTool(workspace),
            SearchCodeTool(workspace),
            BuildSolutionTool(workspace),
            RunTestsTool(workspace),
            GenerateUnitTestTool(workspace, llm),
            AnalyzeLogsTool(seq=seq, log_dir=settings.resolve(settings.observability.log_dir)),
            QueryJiraTool(jira),
            QueryDatabaseTool(query_runner or _unconfigured_runner),
            CreatePullRequestTool(github, workspace),
        ]
    )
    logger.info("registered %d tools: %s", len(registry), ", ".join(registry.names()))
    return registry


__all__ = [
    "AnalyzeLogsTool",
    "BuildSolutionTool",
    "CommandOutcome",
    "CreatePullRequestTool",
    "FunctionTool",
    "GenerateUnitTestTool",
    "JSONSchema",
    "QueryDatabaseTool",
    "QueryJiraTool",
    "QueryRunner",
    "ReadFileTool",
    "RunTestsTool",
    "SearchCodeTool",
    "Tool",
    "ToolError",
    "ToolRegistry",
    "ToolResult",
    "Workspace",
    "WorkspaceError",
    "WriteFileTool",
    "assert_read_only",
    "build_default_tools",
]

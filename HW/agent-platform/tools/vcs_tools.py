"""Version-control tool: open a pull request.

This is the only outward-facing write in the tool set. It opens the PR as a
draft by default so a human reviews before anything merges, and it never pushes
with force.
"""

from __future__ import annotations

import logging

from integrations.github_client import GitHubClient

from .base import Tool, ToolError, ToolResult
from .workspace import Workspace, WorkspaceError

logger = logging.getLogger(__name__)


class CreatePullRequestTool(Tool):
    name = "create_pull_request"
    description = (
        "Push the current branch and open a draft pull request with the given "
        "title and body. Use only after the build is green and the tests pass. "
        "The PR body should state what changed and how it was verified."
    )
    read_only = False
    tags = ("vcs", "delivery")
    input_schema = {
        "type": "object",
        "properties": {
            "title": {"type": "string", "description": "Pull request title."},
            "body": {
                "type": "string",
                "description": "Markdown body: what changed, why, and how it was verified.",
            },
            "branch": {
                "type": "string",
                "description": "Head branch to push. Defaults to the current branch.",
            },
            "base": {"type": "string", "description": "Target branch. Defaults to the repo default."},
            "draft": {"type": "boolean", "description": "Open as a draft (default true)."},
        },
        "required": ["title", "body"],
    }

    def __init__(
        self,
        github: GitHubClient,
        workspace: Workspace,
        *,
        push: bool = True,
    ) -> None:
        self.github = github
        self.workspace = workspace
        self.push = push

    def _execute(
        self,
        title: str,
        body: str,
        branch: str | None = None,
        base: str | None = None,
        draft: bool | None = None,
    ) -> ToolResult:
        if not self.github.configured:
            raise ToolError(
                "GitHub is not configured; set integrations.github_repo and the token"
            )

        head = branch or self._current_branch()
        if head in {"main", "master"}:
            raise ToolError(
                f"refusing to open a pull request from {head!r}; work on a feature branch"
            )

        if self.push:
            pushed = self.workspace.run(f"git push --set-upstream origin {head}")
            if not pushed.ok:
                raise ToolError(f"git push failed: {pushed.tail(20)}")

        try:
            pull_request = self.github.create_pull_request(
                title=title,
                head=head,
                body=body,
                base=base,
                draft=True if draft is None else bool(draft),
            )
        except (ConnectionError, RuntimeError) as exc:
            raise ToolError(str(exc)) from exc

        return ToolResult.success(self.name, pull_request.to_dict(), branch=head)

    def _current_branch(self) -> str:
        try:
            outcome = self.workspace.run("git rev-parse --abbrev-ref HEAD")
        except WorkspaceError as exc:
            raise ToolError(str(exc)) from exc
        if not outcome.ok:
            raise ToolError(f"could not determine the current branch: {outcome.tail(10)}")
        return outcome.stdout.strip()


__all__ = ["CreatePullRequestTool"]

"""Jira tool -- fetch a ticket or run a JQL search."""

from __future__ import annotations

from integrations.jira_client import JiraClient

from .base import Tool, ToolError, ToolResult


class QueryJiraTool(Tool):
    name = "query_jira"
    description = (
        "Read a Jira ticket by key, or search with JQL. Returns the summary, "
        "description, status, components and recent comments. Start a bug fix "
        "here: the reproduction steps and the reporter's exact wording matter."
    )
    read_only = True
    tags = ("jira", "planning")
    input_schema = {
        "type": "object",
        "properties": {
            "issue_key": {
                "type": "string",
                "description": "Ticket key, e.g. BUG-123. Takes precedence over jql.",
            },
            "jql": {
                "type": "string",
                "description": 'JQL query, e.g. project = MES AND status = "In Progress"',
            },
            "limit": {"type": "integer", "description": "Maximum issues for a JQL search."},
        },
        "required": [],
    }

    def __init__(self, client: JiraClient) -> None:
        self.client = client

    def _execute(
        self, issue_key: str | None = None, jql: str | None = None, limit: int | None = None
    ) -> ToolResult:
        if not issue_key and not jql:
            raise ToolError("provide either issue_key or jql")
        if not self.client.configured:
            raise ToolError(
                "Jira is not configured; set integrations.jira_base_url and credentials"
            )

        try:
            if issue_key:
                issue = self.client.get_issue(issue_key.strip())
                return ToolResult.success(
                    self.name,
                    {"issue": issue.to_dict(), "prompt": issue.as_prompt()},
                    issue_key=issue.key,
                )
            issues = self.client.search(jql or "", limit=limit or 20)
            return ToolResult.success(
                self.name,
                {"count": len(issues), "issues": [issue.to_dict() for issue in issues]},
                jql=jql,
            )
        except (ConnectionError, RuntimeError) as exc:
            raise ToolError(str(exc)) from exc


__all__ = ["QueryJiraTool"]

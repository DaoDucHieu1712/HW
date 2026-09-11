"""Jira Cloud REST adapter.

Only the read paths the workflows need are implemented: fetch an issue, search
with JQL, read comments. Writing back to Jira is deliberately out of scope --
status transitions belong to a human.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass
from typing import Any

from configs.settings import IntegrationSettings

from .http import HttpClient

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class JiraIssue:
    """The subset of an issue the agents actually reason about."""

    key: str
    summary: str
    description: str
    status: str
    issue_type: str
    priority: str
    assignee: str | None
    reporter: str | None
    labels: list[str]
    components: list[str]
    comments: list[str]

    def to_dict(self) -> dict[str, Any]:
        return {
            "key": self.key,
            "summary": self.summary,
            "description": self.description,
            "status": self.status,
            "issue_type": self.issue_type,
            "priority": self.priority,
            "assignee": self.assignee,
            "reporter": self.reporter,
            "labels": self.labels,
            "components": self.components,
            "comments": self.comments,
        }

    def as_prompt(self) -> str:
        """Render the issue as the context block handed to an agent."""
        parts = [
            f"# {self.key}: {self.summary}",
            f"Type: {self.issue_type} | Status: {self.status} | Priority: {self.priority}",
        ]
        if self.components:
            parts.append("Components: " + ", ".join(self.components))
        parts.append("\n## Description\n" + (self.description or "(empty)"))
        if self.comments:
            parts.append("\n## Comments\n" + "\n---\n".join(self.comments[-5:]))
        return "\n".join(parts)


class JiraClient:
    """Read-only Jira client. Raises ``ConnectionError`` when unconfigured."""

    def __init__(
        self, settings: IntegrationSettings, *, http: HttpClient | None = None
    ) -> None:
        self.settings = settings
        self._http = http or self._build(settings)

    @staticmethod
    def _build(settings: IntegrationSettings) -> HttpClient | None:
        if not settings.jira_base_url:
            return None
        base = settings.jira_base_url.rstrip("/") + "/rest/api/3"
        client = HttpClient(base_url=base)
        email = os.getenv(settings.jira_email_env)
        token = os.getenv(settings.jira_token_env)
        if email and token:
            client.with_basic(email, token)
        else:
            logger.warning(
                "Jira credentials missing (%s / %s); calls will fail",
                settings.jira_email_env,
                settings.jira_token_env,
            )
        return client

    @property
    def configured(self) -> bool:
        return self._http is not None

    def _require(self) -> HttpClient:
        if self._http is None:
            raise ConnectionError(
                "Jira is not configured; set integrations.jira_base_url and the "
                "credential environment variables"
            )
        return self._http

    def get_issue(self, key: str) -> JiraIssue:
        data = self._require().get(
            f"issue/{key}", params={"fields": "*all", "expand": "renderedFields"}
        )
        return self._parse(data or {})

    def search(self, jql: str, *, limit: int = 20) -> list[JiraIssue]:
        data = self._require().post(
            "search", body={"jql": jql, "maxResults": limit, "fields": ["*all"]}
        )
        return [self._parse(issue) for issue in (data or {}).get("issues", [])]

    # -- parsing ---------------------------------------------------------

    @classmethod
    def _parse(cls, data: dict[str, Any]) -> JiraIssue:
        fields = data.get("fields") or {}
        raw_comments = (fields.get("comment") or {}).get("comments", [])
        comments = [cls._plain_text(comment.get("body")) for comment in raw_comments]
        return JiraIssue(
            key=data.get("key", "UNKNOWN"),
            summary=fields.get("summary", ""),
            description=cls._plain_text(fields.get("description")),
            status=(fields.get("status") or {}).get("name") or "unknown",
            issue_type=(fields.get("issuetype") or {}).get("name") or "unknown",
            priority=(fields.get("priority") or {}).get("name") or "unset",
            assignee=(fields.get("assignee") or {}).get("displayName"),
            reporter=(fields.get("reporter") or {}).get("displayName"),
            labels=list(fields.get("labels") or []),
            components=[c.get("name", "") for c in (fields.get("components") or [])],
            comments=[c for c in comments if c],
        )

    @staticmethod
    def _plain_text(node: Any) -> str:
        """Flatten Atlassian Document Format into plain text."""
        if node is None:
            return ""
        if isinstance(node, str):
            return node
        if isinstance(node, list):
            return "\n".join(JiraClient._plain_text(item) for item in node)
        if isinstance(node, dict):
            if node.get("type") == "text":
                return str(node.get("text", ""))
            return JiraClient._plain_text(node.get("content", []))
        return str(node)


__all__ = ["JiraClient", "JiraIssue"]

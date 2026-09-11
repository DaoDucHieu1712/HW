"""GitHub REST adapter -- just enough to open a pull request.

Pull request creation is the one outward-facing write the platform performs, so
it is deliberately narrow: draft by default, and it refuses to run without an
explicitly configured repository and token.
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
class PullRequest:
    number: int
    url: str
    title: str
    draft: bool
    branch: str

    def to_dict(self) -> dict[str, Any]:
        return {
            "number": self.number,
            "url": self.url,
            "title": self.title,
            "draft": self.draft,
            "branch": self.branch,
        }


class GitHubClient:
    """Read/write GitHub client scoped to a single repository."""

    def __init__(
        self, settings: IntegrationSettings, *, http: HttpClient | None = None
    ) -> None:
        self.settings = settings
        self.repo = settings.github_repo
        self._http = http or self._build(settings)

    @staticmethod
    def _build(settings: IntegrationSettings) -> HttpClient | None:
        if not settings.github_repo:
            return None
        client = HttpClient(base_url=settings.github_api_url)
        client.headers["Accept"] = "application/vnd.github+json"
        client.headers["X-GitHub-Api-Version"] = "2022-11-28"
        token = os.getenv(settings.github_token_env)
        if token:
            client.with_bearer(token)
        else:
            logger.warning(
                "GitHub token missing (%s); write calls will fail",
                settings.github_token_env,
            )
        return client

    @property
    def configured(self) -> bool:
        return self._http is not None and bool(self.repo)

    def _require(self) -> HttpClient:
        if not self.configured:
            raise ConnectionError(
                "GitHub is not configured; set integrations.github_repo and the token"
            )
        assert self._http is not None  # narrowed by `configured`
        return self._http

    def default_branch(self) -> str:
        data = self._require().get(f"repos/{self.repo}")
        return (data or {}).get("default_branch", "main")

    def create_pull_request(
        self,
        *,
        title: str,
        head: str,
        body: str,
        base: str | None = None,
        draft: bool = True,
    ) -> PullRequest:
        """Open a PR from ``head`` into ``base`` (default branch when omitted)."""
        target = base or self.default_branch()
        data = self._require().post(
            f"repos/{self.repo}/pulls",
            body={
                "title": title,
                "head": head,
                "base": target,
                "body": body,
                "draft": draft,
            },
        )
        payload = data or {}
        return PullRequest(
            number=int(payload.get("number", 0)),
            url=payload.get("html_url", ""),
            title=payload.get("title", title),
            draft=bool(payload.get("draft", draft)),
            branch=head,
        )


__all__ = ["GitHubClient", "PullRequest"]

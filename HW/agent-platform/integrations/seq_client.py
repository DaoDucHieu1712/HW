"""Seq structured-log adapter.

Seq speaks a filter language over structured events, which is exactly what the
log-analysis workflow needs: pull the events for one correlation id, keep the
original exception, and let the agent reconstruct the timeline from them.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import Any, Iterable

from configs.settings import IntegrationSettings

from .http import HttpClient

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class LogEvent:
    """One structured log event, flattened for prompting."""

    timestamp: str
    level: str
    message: str
    exception: str | None
    correlation_id: str | None
    properties: dict[str, Any]

    def to_dict(self) -> dict[str, Any]:
        return {
            "timestamp": self.timestamp,
            "level": self.level,
            "message": self.message,
            "exception": self.exception,
            "correlation_id": self.correlation_id,
            "properties": self.properties,
        }

    def as_line(self) -> str:
        head = f"{self.timestamp} [{self.level}] {self.message}"
        return f"{head}\n{self.exception}" if self.exception else head


class SeqClient:
    """Read-only Seq client."""

    def __init__(
        self, settings: IntegrationSettings, *, http: HttpClient | None = None
    ) -> None:
        self.settings = settings
        self._http = http or self._build(settings)

    @staticmethod
    def _build(settings: IntegrationSettings) -> HttpClient | None:
        if not settings.seq_base_url:
            return None
        client = HttpClient(base_url=settings.seq_base_url)
        api_key = os.getenv(settings.seq_api_key_env)
        if api_key:
            client.headers["X-Seq-ApiKey"] = api_key
        return client

    @property
    def configured(self) -> bool:
        return self._http is not None

    def _require(self) -> HttpClient:
        if self._http is None:
            raise ConnectionError(
                "Seq is not configured; set integrations.seq_base_url"
            )
        return self._http

    def events(
        self,
        *,
        filter_expression: str | None = None,
        since_minutes: int = 60,
        level: str | None = None,
        limit: int = 100,
    ) -> list[LogEvent]:
        """Fetch events matching a Seq filter within a time window."""
        clauses = [filter_expression] if filter_expression else []
        if level:
            clauses.append(f"@Level = '{level}'")
        start = datetime.now(timezone.utc) - timedelta(minutes=since_minutes)

        data = self._require().get(
            "api/events",
            params={
                "filter": " and ".join(c for c in clauses if c) or None,
                "count": limit,
                "fromDateUtc": start.isoformat(),
                "render": "true",
            },
        )
        return list(self._parse_all(data or []))

    def by_correlation_id(self, correlation_id: str, *, limit: int = 200) -> list[LogEvent]:
        """Every event that shares one correlation id, oldest first.

        This is the anchor of an incident timeline: cause and consequence are
        only separable once the whole request is on screen in order.
        """
        events = self.events(
            filter_expression=f"CorrelationId = '{correlation_id}'",
            since_minutes=24 * 60,
            limit=limit,
        )
        return sorted(events, key=lambda event: event.timestamp)

    # -- parsing ---------------------------------------------------------

    @classmethod
    def _parse_all(cls, payload: Any) -> Iterable[LogEvent]:
        rows = payload.get("Events", []) if isinstance(payload, dict) else payload
        for row in rows or []:
            yield cls._parse(row)

    @staticmethod
    def _parse(row: dict[str, Any]) -> LogEvent:
        properties = {
            prop.get("Name"): prop.get("Value")
            for prop in row.get("Properties", []) or []
            if isinstance(prop, dict)
        }
        tokens = row.get("MessageTemplateTokens", []) or []
        message = row.get("RenderedMessage") or "".join(
            str(token.get("Text", "")) for token in tokens if isinstance(token, dict)
        )
        return LogEvent(
            timestamp=row.get("Timestamp", ""),
            level=row.get("Level", "Information"),
            message=message,
            exception=row.get("Exception"),
            correlation_id=properties.get("CorrelationId"),
            properties=properties,
        )


__all__ = ["LogEvent", "SeqClient"]

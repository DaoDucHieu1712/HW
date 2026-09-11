"""Tiny JSON-over-HTTP client used by the integration adapters.

Built on ``urllib`` from the standard library on purpose: the integration
surface is small enough that adding an HTTP library for it is not worth the
extra supply-chain footprint.
"""

from __future__ import annotations

import base64
import json
import logging
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from typing import Any, Mapping

logger = logging.getLogger(__name__)


class HttpError(RuntimeError):
    """Non-2xx response or transport failure."""

    def __init__(self, message: str, *, status: int | None = None, body: str = "") -> None:
        super().__init__(message)
        self.status = status
        self.body = body


@dataclass(slots=True)
class HttpClient:
    """Minimal JSON client: base URL, default headers, timeout."""

    base_url: str
    headers: dict[str, str] = field(default_factory=dict)
    timeout: float = 30.0

    def with_bearer(self, token: str) -> "HttpClient":
        self.headers["Authorization"] = f"Bearer {token}"
        return self

    def with_basic(self, user: str, password: str) -> "HttpClient":
        raw = f"{user}:{password}".encode("utf-8")
        self.headers["Authorization"] = "Basic " + base64.b64encode(raw).decode("ascii")
        return self

    def _url(self, path: str, params: Mapping[str, Any] | None = None) -> str:
        if path.startswith("http"):
            url = path
        else:
            url = self.base_url.rstrip("/") + "/" + path.lstrip("/")
        if params:
            clean = {key: value for key, value in params.items() if value is not None}
            if clean:
                url = url + "?" + urllib.parse.urlencode(clean, doseq=True)
        return url

    def request(
        self,
        method: str,
        path: str,
        *,
        params: Mapping[str, Any] | None = None,
        body: Any = None,
        headers: Mapping[str, str] | None = None,
    ) -> Any:
        url = self._url(path, params)
        payload = None
        merged = {"Accept": "application/json", **self.headers, **(headers or {})}
        if body is not None:
            payload = json.dumps(body).encode("utf-8")
            merged.setdefault("Content-Type", "application/json")

        request = urllib.request.Request(
            url, data=payload, headers=merged, method=method.upper()
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                raw = response.read().decode("utf-8", errors="replace")
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")[:2000]
            raise HttpError(
                f"{method.upper()} {url} failed: HTTP {exc.code}",
                status=exc.code,
                body=detail,
            ) from exc
        except urllib.error.URLError as exc:
            raise HttpError(f"{method.upper()} {url} failed: {exc.reason}") from exc

        if not raw:
            return None
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            return raw

    def get(self, path: str, **kwargs: Any) -> Any:
        return self.request("GET", path, **kwargs)

    def post(self, path: str, **kwargs: Any) -> Any:
        return self.request("POST", path, **kwargs)


__all__ = ["HttpClient", "HttpError"]

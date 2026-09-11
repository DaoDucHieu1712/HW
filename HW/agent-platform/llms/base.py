"""Provider-neutral LLM contract.

Agents depend on :class:`LLMClient`, never on a vendor SDK. That keeps the
dependency arrow pointing inward (agents -> abstraction <- provider adapter) and
makes every agent testable against :class:`llms.fake.EchoLLM`.
"""

from __future__ import annotations

import abc
import json
import re
from dataclasses import dataclass, field
from typing import Any, Iterable, Literal, Sequence

Role = Literal["user", "assistant", "system"]


@dataclass(slots=True)
class LLMMessage:
    """One turn of the conversation.

    ``content`` is either a plain string or the API's content-block list; the
    block form is required when replaying tool use back to the model.
    """

    role: Role
    content: str | list[dict[str, Any]]

    def to_api(self) -> dict[str, Any]:
        return {"role": self.role, "content": self.content}

    @classmethod
    def user(cls, content: str | list[dict[str, Any]]) -> "LLMMessage":
        return cls(role="user", content=content)

    @classmethod
    def assistant(cls, content: str | list[dict[str, Any]]) -> "LLMMessage":
        return cls(role="assistant", content=content)


@dataclass(slots=True)
class ToolCall:
    """A ``tool_use`` block the model emitted."""

    id: str
    name: str
    arguments: dict[str, Any]


@dataclass(slots=True)
class LLMResponse:
    """Normalised result of one Messages API call."""

    text: str
    tool_calls: list[ToolCall] = field(default_factory=list)
    stop_reason: str | None = None
    #: Raw assistant content blocks, needed verbatim when continuing the turn.
    raw_content: list[dict[str, Any]] = field(default_factory=list)
    usage: dict[str, int] = field(default_factory=dict)
    model: str = ""

    @property
    def wants_tools(self) -> bool:
        return bool(self.tool_calls)

    def json(self, *, strict: bool = False) -> dict[str, Any]:
        """Parse the response text as JSON.

        Models wrap JSON in prose or fences often enough that a tolerant parse
        is worth the few lines; ``strict=True`` opts out of the salvage step.
        """
        return parse_json_payload(self.text, strict=strict)


_FENCE = re.compile(r"```(?:json|jsonc)?\s*(.+?)```", re.DOTALL)


def parse_json_payload(text: str, *, strict: bool = False) -> dict[str, Any]:
    """Extract a JSON object from model output.

    Tries, in order: the whole string, a fenced block, then the outermost
    ``{...}`` span. Returns ``{}`` when nothing parses and ``strict`` is off.
    """
    candidates: list[str] = [text.strip()]
    fenced = _FENCE.search(text)
    if fenced:
        candidates.append(fenced.group(1).strip())
    start, end = text.find("{"), text.rfind("}")
    if start != -1 and end > start:
        candidates.append(text[start : end + 1])

    for candidate in candidates:
        if not candidate:
            continue
        try:
            parsed = json.loads(candidate)
        except json.JSONDecodeError:
            continue
        if isinstance(parsed, dict):
            return parsed
        return {"value": parsed}

    if strict:
        raise ValueError("response did not contain a JSON object")
    return {}


class LLMClient(abc.ABC):
    """Minimal surface every provider adapter must implement."""

    #: Model id in use, for logging and cost attribution.
    model: str = ""

    @abc.abstractmethod
    def complete(
        self,
        messages: Sequence[LLMMessage],
        *,
        system: str | None = None,
        tools: Sequence[dict[str, Any]] | None = None,
        max_tokens: int | None = None,
        effort: str | None = None,
        stream: bool | None = None,
    ) -> LLMResponse:
        """Single Messages API round trip -- no tool execution."""

    @abc.abstractmethod
    def complete_structured(
        self,
        messages: Sequence[LLMMessage],
        *,
        schema: dict[str, Any],
        system: str | None = None,
        max_tokens: int | None = None,
        effort: str | None = None,
    ) -> dict[str, Any]:
        """Return a dict conforming to ``schema`` (structured outputs)."""

    def close(self) -> None:  # pragma: no cover - adapters override if needed
        """Release provider resources."""


def system_blocks(parts: Iterable[str], *, cache_last: bool = True) -> list[dict[str, Any]]:
    """Build a system prompt as cacheable blocks.

    Stable content goes first and carries the cache breakpoint, so the volatile
    task text appended later never invalidates the cached prefix.
    """
    blocks: list[dict[str, Any]] = [
        {"type": "text", "text": part} for part in parts if part and part.strip()
    ]
    if blocks and cache_last:
        blocks[-1]["cache_control"] = {"type": "ephemeral"}
    return blocks


__all__ = [
    "LLMClient",
    "LLMMessage",
    "LLMResponse",
    "Role",
    "ToolCall",
    "parse_json_payload",
    "system_blocks",
]

"""Deterministic offline LLM used by tests and by ``dry_run`` mode.

It never touches the network. Given a queue of canned replies it returns them in
order; otherwise it synthesises a reply that satisfies the JSON schema the agent
put in its system prompt.

That synthesised reply reports *success* -- the build compiled, the tests passed,
the review approved. It is a wiring check, not a verdict: dry-run mode proves
every node executes and every patch merges, and says nothing whatsoever about
the code. Never read a dry-run result as evidence about a change.
"""

from __future__ import annotations

import json
import re
from collections import deque
from typing import Any, Iterable, Sequence

from .base import LLMClient, LLMMessage, LLMResponse, ToolCall, parse_json_payload

#: Boolean fields that mean "this step went well". The stub answers true so the
#: happy path through the graph is exercisable offline.
_OPTIMISTIC_FIELDS = {
    "approved",
    "build_succeeded",
    "tests_passed",
    "would_have_caught_the_bug",
}

_SCHEMA_FENCE = re.compile(r"```json\s*(\{.+?\})\s*```", re.DOTALL)


class EchoLLM(LLMClient):
    """Scriptable stand-in for :class:`llms.claude.ClaudeLLM`."""

    def __init__(
        self,
        replies: Iterable[str | LLMResponse] | None = None,
        *,
        model: str = "echo-llm",
    ) -> None:
        self.model = model
        self._replies: deque[str | LLMResponse] = deque(replies or ())
        #: Every request, recorded for assertions in tests.
        self.calls: list[dict[str, Any]] = []

    def queue(self, *replies: str | LLMResponse) -> "EchoLLM":
        """Add scripted replies, returned in order before any synthesis."""
        self._replies.extend(replies)
        return self

    # -- LLMClient -------------------------------------------------------

    def complete(
        self,
        messages: Sequence[LLMMessage],
        *,
        system: str | list[dict[str, Any]] | None = None,
        tools: Sequence[dict[str, Any]] | None = None,
        max_tokens: int | None = None,
        effort: str | None = None,
        stream: bool | None = None,
    ) -> LLMResponse:
        self.calls.append(
            {
                "messages": [message.to_api() for message in messages],
                "system": system,
                "tools": [tool.get("name") for tool in (tools or [])],
                "effort": effort,
            }
        )
        if self._replies:
            reply = self._replies.popleft()
            if isinstance(reply, LLMResponse):
                return reply
            return _text_response(reply, self.model)

        return _text_response(self._synthesise(messages, system), self.model)

    def complete_structured(
        self,
        messages: Sequence[LLMMessage],
        *,
        schema: dict[str, Any],
        system: str | list[dict[str, Any]] | None = None,
        max_tokens: int | None = None,
        effort: str | None = None,
    ) -> dict[str, Any]:
        response = self.complete(
            messages, system=system, max_tokens=max_tokens, effort=effort
        )
        return response.json() or stub_from_schema(schema)

    # -- synthesis -------------------------------------------------------

    def _synthesise(
        self, messages: Sequence[LLMMessage], system: str | list[dict[str, Any]] | None
    ) -> str:
        """Build a reply that satisfies the schema in the system prompt."""
        schema = extract_schema(system)
        last = messages[-1].content if messages else ""
        summary = last if isinstance(last, str) else json.dumps(last, default=str)

        payload = stub_from_schema(schema) if schema else {}
        payload.setdefault("summary", "")
        payload["summary"] = f"[dry-run] {summary.strip()[:280]}"
        return json.dumps(payload, indent=2)


def extract_schema(system: str | list[dict[str, Any]] | None) -> dict[str, Any] | None:
    """Pull the JSON Schema an agent embedded in its system prompt."""
    if system is None:
        return None
    text = (
        system
        if isinstance(system, str)
        else "\n".join(str(block.get("text", "")) for block in system)
    )
    match = _SCHEMA_FENCE.search(text)
    if not match:
        return None
    try:
        schema = json.loads(match.group(1))
    except json.JSONDecodeError:
        return None
    return schema if isinstance(schema, dict) else None


def stub_from_schema(schema: dict[str, Any], *, depth: int = 0) -> Any:
    """Synthesise the smallest value satisfying ``schema``.

    Only required properties are produced: an agent's optional fields are
    genuinely optional, and filling them would mask a workflow that depends on
    something it never asked for.
    """
    if depth > 6:  # pragma: no cover - schemas are not this deep
        return None

    if "enum" in schema and schema["enum"]:
        return schema["enum"][0]

    kind = schema.get("type", "object")
    if kind == "object":
        properties: dict[str, Any] = schema.get("properties", {}) or {}
        required = schema.get("required") or list(properties)
        result: dict[str, Any] = {}
        for name in required:
            body = properties.get(name, {"type": "string"})
            if name in _OPTIMISTIC_FIELDS and body.get("type") == "boolean":
                result[name] = True
            else:
                result[name] = stub_from_schema(body, depth=depth + 1)
        return result
    if kind == "array":
        # Empty, not one synthetic element: a stubbed "finding" would route the
        # graph as though a real defect had been found.
        return []
    if kind == "boolean":
        return False
    if kind in {"integer", "number"}:
        return 0
    return ""


def tool_call_response(
    name: str, arguments: dict[str, Any], *, call_id: str = "call_1"
) -> LLMResponse:
    """Build a response that asks for one tool call -- handy in tests."""
    return LLMResponse(
        text="",
        tool_calls=[ToolCall(id=call_id, name=name, arguments=arguments)],
        stop_reason="tool_use",
        raw_content=[
            {"type": "tool_use", "id": call_id, "name": name, "input": arguments}
        ],
        model="echo-llm",
    )


def json_response(payload: dict[str, Any]) -> LLMResponse:
    """Build a plain JSON reply -- the shape agents expect."""
    text = json.dumps(payload)
    return _text_response(text, "echo-llm")


def _text_response(text: str, model: str) -> LLMResponse:
    return LLMResponse(
        text=text,
        stop_reason="end_turn",
        raw_content=[{"type": "text", "text": text}],
        model=model,
    )


__all__ = [
    "EchoLLM",
    "extract_schema",
    "json_response",
    "parse_json_payload",
    "stub_from_schema",
    "tool_call_response",
]

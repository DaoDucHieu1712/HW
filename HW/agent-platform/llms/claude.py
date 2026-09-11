"""Claude adapter built on the official ``anthropic`` Python SDK.

Model behaviour that this adapter encodes so callers do not have to:

* Adaptive thinking (``{"type": "adaptive"}``) is the only supported on-mode on
  Claude Sonnet 5 / Opus 5; ``budget_tokens`` is rejected with a 400.
* Effort is ``output_config.effort``, not a top-level parameter.
* Assistant prefill is not supported on these models -- shape the output with
  structured outputs or system instructions instead.
* Large ``max_tokens`` must stream, or the request risks an HTTP timeout.
"""

from __future__ import annotations

import logging
from typing import Any, Sequence

from configs.settings import LLMSettings
from hooks.events import HookEvent
from hooks.manager import HookManager

from .base import LLMClient, LLMMessage, LLMResponse, ToolCall

logger = logging.getLogger(__name__)

#: Above this many output tokens the SDK is asked to stream, so the request
#: cannot trip the HTTP timeout.
STREAM_THRESHOLD_TOKENS = 8192


class ClaudeLLM(LLMClient):
    """Thin, hook-aware wrapper over ``client.messages``."""

    def __init__(
        self,
        settings: LLMSettings,
        *,
        hooks: HookManager | None = None,
        client: Any | None = None,
        model: str | None = None,
    ) -> None:
        self.settings = settings
        self.hooks = hooks
        self.model = model or settings.model
        self._client = client or self._build_client(settings)

    # -- construction ----------------------------------------------------

    @staticmethod
    def _build_client(settings: LLMSettings) -> Any:
        try:
            import anthropic
        except ImportError as exc:  # pragma: no cover - install-time failure
            raise RuntimeError(
                "the anthropic package is required; run: pip install -r requirements.txt"
            ) from exc
        # The zero-arg constructor resolves ANTHROPIC_API_KEY, ANTHROPIC_AUTH_TOKEN
        # or an `ant auth login` profile -- do not force a key in here.
        return anthropic.Anthropic(
            timeout=settings.timeout_seconds,
            max_retries=settings.max_retries,
        )

    # -- request building ------------------------------------------------

    def _base_request(
        self,
        messages: Sequence[LLMMessage],
        *,
        system: str | list[dict[str, Any]] | None,
        max_tokens: int | None,
        effort: str | None,
    ) -> dict[str, Any]:
        request: dict[str, Any] = {
            "model": self.model,
            "max_tokens": max_tokens or self.settings.max_tokens,
            "messages": [m.to_api() for m in messages],
        }
        if system:
            request["system"] = system
        if self.settings.thinking:
            request["thinking"] = {
                "type": "adaptive",
                "display": self.settings.thinking_display,
            }
        chosen_effort = effort or self.settings.effort
        if chosen_effort:
            request["output_config"] = {"effort": chosen_effort}
        return request

    def _send(self, request: dict[str, Any], *, stream: bool) -> Any:
        if stream:
            with self._client.messages.stream(**request) as handle:
                return handle.get_final_message()
        return self._client.messages.create(**request)

    # -- public API ------------------------------------------------------

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
        request = self._base_request(
            messages, system=system, max_tokens=max_tokens, effort=effort
        )
        if tools:
            request["tools"] = list(tools)

        should_stream = (
            stream
            if stream is not None
            else request["max_tokens"] > STREAM_THRESHOLD_TOKENS
        )

        if self.hooks:
            context = self.hooks.emit(
                HookEvent.BEFORE_LLM_CALL,
                self.model,
                model=self.model,
                messages=len(request["messages"]),
                tools=[t.get("name") for t in request.get("tools", [])],
                effort=(request.get("output_config") or {}).get("effort"),
            )
            if context.cancelled:
                raise RuntimeError(f"LLM call vetoed by hook: {context.cancel_reason}")

        try:
            message = self._send(request, stream=should_stream)
        except Exception as exc:
            if self.hooks:
                self.hooks.emit_error(self.model, exc, model=self.model)
            raise

        response = self._normalise(message)

        if self.hooks:
            self.hooks.emit(
                HookEvent.AFTER_LLM_CALL,
                self.model,
                model=self.model,
                stop_reason=response.stop_reason,
                usage=response.usage,
                tool_calls=[call.name for call in response.tool_calls],
            )
        return response

    def complete_structured(
        self,
        messages: Sequence[LLMMessage],
        *,
        schema: dict[str, Any],
        system: str | list[dict[str, Any]] | None = None,
        max_tokens: int | None = None,
        effort: str | None = None,
    ) -> dict[str, Any]:
        """Constrain the reply to ``schema`` via ``output_config.format``."""
        request = self._base_request(
            messages, system=system, max_tokens=max_tokens, effort=effort
        )
        request.setdefault("output_config", {})["format"] = {
            "type": "json_schema",
            "schema": schema,
        }
        message = self._send(
            request, stream=request["max_tokens"] > STREAM_THRESHOLD_TOKENS
        )
        return self._normalise(message).json()

    # -- normalisation ---------------------------------------------------

    @staticmethod
    def _normalise(message: Any) -> LLMResponse:
        """Flatten SDK content blocks into an :class:`LLMResponse`.

        ``stop_details`` is populated only when ``stop_reason == "refusal"``, so
        it is read defensively.
        """
        text_parts: list[str] = []
        tool_calls: list[ToolCall] = []
        raw_content: list[dict[str, Any]] = []

        for block in getattr(message, "content", None) or []:
            block_type = getattr(block, "type", None)
            if block_type == "text":
                text_parts.append(block.text)
            elif block_type == "tool_use":
                tool_calls.append(
                    ToolCall(
                        id=block.id,
                        name=block.name,
                        arguments=dict(block.input or {}),
                    )
                )
            raw_content.append(_block_to_dict(block))

        stop_reason = getattr(message, "stop_reason", None)
        if stop_reason == "refusal":
            details = getattr(message, "stop_details", None)
            logger.warning(
                "model refused the request (category=%s)",
                getattr(details, "category", None),
            )

        usage_obj = getattr(message, "usage", None)
        usage: dict[str, int] = {}
        if usage_obj is not None:
            for key in (
                "input_tokens",
                "output_tokens",
                "cache_read_input_tokens",
                "cache_creation_input_tokens",
            ):
                usage[key] = int(getattr(usage_obj, key, 0) or 0)

        return LLMResponse(
            text="\n".join(text_parts).strip(),
            tool_calls=tool_calls,
            stop_reason=stop_reason,
            raw_content=raw_content,
            usage=usage,
            model=getattr(message, "model", ""),
        )

    def close(self) -> None:
        close = getattr(self._client, "close", None)
        if callable(close):  # pragma: no cover - SDK housekeeping
            close()


def _block_to_dict(block: Any) -> dict[str, Any]:
    """Convert an SDK content block to the plain dict the API accepts back."""
    dumper = getattr(block, "model_dump", None)
    if callable(dumper):
        return dumper(exclude_none=True)
    if isinstance(block, dict):  # pragma: no cover - already plain
        return block
    return {"type": getattr(block, "type", "text"), "text": str(block)}


__all__ = ["ClaudeLLM", "STREAM_THRESHOLD_TOKENS"]

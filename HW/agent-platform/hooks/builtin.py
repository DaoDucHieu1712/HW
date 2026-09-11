"""Hook handlers shipped with the platform.

These are registered by :func:`install_default_hooks` during container build.
Everything here is optional -- drop a handler and the platform still runs, it
just stops recording or guarding that dimension.
"""

from __future__ import annotations

import json
import logging
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable

from .events import HookContext, HookEvent
from .manager import HookManager

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class UsageMeter:
    """Accumulates token spend and call counts for one process/run."""

    input_tokens: int = 0
    output_tokens: int = 0
    cache_read_tokens: int = 0
    llm_calls: int = 0
    tool_calls: int = 0
    agent_calls: int = 0
    errors: int = 0
    per_agent_ms: dict[str, float] = field(default_factory=dict)

    def snapshot(self) -> dict[str, Any]:
        return {
            "llm_calls": self.llm_calls,
            "tool_calls": self.tool_calls,
            "agent_calls": self.agent_calls,
            "errors": self.errors,
            "input_tokens": self.input_tokens,
            "output_tokens": self.output_tokens,
            "cache_read_tokens": self.cache_read_tokens,
            "per_agent_ms": dict(self.per_agent_ms),
        }

    # -- handlers --------------------------------------------------------

    def on_after_llm(self, context: HookContext) -> None:
        self.llm_calls += 1
        usage = (context.data.get("usage") or {}) if isinstance(context.data, dict) else {}
        self.input_tokens += int(usage.get("input_tokens") or 0)
        self.output_tokens += int(usage.get("output_tokens") or 0)
        self.cache_read_tokens += int(usage.get("cache_read_input_tokens") or 0)

    def on_after_tool(self, context: HookContext) -> None:
        self.tool_calls += 1

    def on_after_agent(self, context: HookContext) -> None:
        self.agent_calls += 1
        self.per_agent_ms[context.source] = (
            self.per_agent_ms.get(context.source, 0.0) + context.elapsed_ms
        )

    def on_error(self, context: HookContext) -> None:
        self.errors += 1


class AuditTrail:
    """Append-only JSONL record of everything the platform did.

    One line per event keeps the trail greppable and cheap to ship to Seq or a
    log pipeline without a parser.
    """

    def __init__(self, path: str | Path, *, redact_keys: Iterable[str] = ()) -> None:
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._redact = {key.lower() for key in redact_keys} | {
            "token",
            "api_key",
            "password",
            "secret",
            "authorization",
        }

    def _scrub(self, value: Any, depth: int = 0) -> Any:
        if depth > 4:
            return "<truncated>"
        if isinstance(value, dict):
            return {
                key: ("<redacted>" if key.lower() in self._redact else self._scrub(item, depth + 1))
                for key, item in value.items()
            }
        if isinstance(value, (list, tuple)):
            return [self._scrub(item, depth + 1) for item in value[:20]]
        if isinstance(value, str) and len(value) > 500:
            return value[:500] + f"... <+{len(value) - 500} chars>"
        if isinstance(value, (int, float, bool)) or value is None:
            return value
        return repr(value)[:500]

    def __call__(self, context: HookContext) -> None:
        record = {
            "ts": time.time(),
            "event": context.event.value,
            "source": context.source,
            "correlation_id": context.correlation_id,
            "elapsed_ms": round(context.elapsed_ms, 2),
            "data": self._scrub(context.data),
        }
        if context.error is not None:
            record["error"] = f"{type(context.error).__name__}: {context.error}"
        with self.path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(record, default=str) + "\n")


def log_hook(context: HookContext) -> None:
    """Human-readable trace of the event stream."""
    logger.info(
        "%-22s %-22s %s",
        context.event.value,
        context.source,
        {k: v for k, v in context.data.items() if k in {"tool", "agent", "model", "status"}},
    )


class ToolGuard:
    """Blocks tool calls that violate policy before they execute.

    This is the deterministic boundary: it inspects the *call*, never the
    prompt, so a prompt-injected instruction cannot talk it out of a veto.
    """

    def __init__(self, *, denied_tools: Iterable[str] = (), allow_writes: bool = True) -> None:
        self.denied_tools = {name.lower() for name in denied_tools}
        self.allow_writes = allow_writes
        self.write_tools = {"write_file", "create_pull_request"}

    def __call__(self, context: HookContext) -> None:
        tool = str(context.data.get("tool", "")).lower()
        if tool in self.denied_tools:
            context.cancel(f"tool '{tool}' is denied by policy")
            return
        if not self.allow_writes and tool in self.write_tools:
            context.cancel(f"tool '{tool}' is disabled: the platform is in read-only mode")


class RetryBudget:
    """Caps how many times a single run may re-enter the coding loop."""

    def __init__(self, max_errors: int = 10) -> None:
        self.max_errors = max_errors
        self._seen = 0

    def __call__(self, context: HookContext) -> None:
        self._seen += 1
        if self._seen > self.max_errors:
            context.cancel(f"error budget exhausted after {self._seen} failures")


def install_default_hooks(
    hooks: HookManager,
    *,
    audit_path: str | Path | None = None,
    allow_writes: bool = True,
    denied_tools: Iterable[str] = (),
) -> UsageMeter:
    """Wire the standard observability + guard handlers. Returns the meter."""
    meter = UsageMeter()

    hooks.register(HookEvent.AFTER_LLM_CALL, meter.on_after_llm, name="usage.llm", priority=10)
    hooks.register(HookEvent.AFTER_TOOL_EXECUTION, meter.on_after_tool, name="usage.tool", priority=10)
    hooks.register(HookEvent.AFTER_AGENT_EXECUTION, meter.on_after_agent, name="usage.agent", priority=10)
    hooks.register(HookEvent.ON_ERROR, meter.on_error, name="usage.error", priority=10)

    guard = ToolGuard(denied_tools=denied_tools, allow_writes=allow_writes)
    hooks.register(HookEvent.BEFORE_TOOL_EXECUTION, guard, name="guard.tool", priority=1)

    for event in (
        HookEvent.BEFORE_AGENT_EXECUTION,
        HookEvent.AFTER_AGENT_EXECUTION,
        HookEvent.BEFORE_TOOL_EXECUTION,
        HookEvent.ON_ERROR,
    ):
        hooks.register(event, log_hook, name=f"log.{event.value}", priority=200)

    if audit_path is not None:
        trail = AuditTrail(audit_path)
        for event in HookEvent:
            hooks.register(event, trail, name=f"audit.{event.value}", priority=250)

    return meter


__all__ = [
    "AuditTrail",
    "RetryBudget",
    "ToolGuard",
    "UsageMeter",
    "install_default_hooks",
    "log_hook",
]

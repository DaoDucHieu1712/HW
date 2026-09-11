"""Hook event taxonomy and the payload carried to every handler."""

from __future__ import annotations

import time
import uuid
from dataclasses import dataclass, field
from enum import Enum
from typing import Any


class HookEvent(str, Enum):
    """Every extension point the runtime emits.

    The value is the wire name used in configuration and logs, so it is part of
    the public contract -- rename with care.
    """

    BEFORE_AGENT_EXECUTION = "BeforeAgentExecution"
    AFTER_AGENT_EXECUTION = "AfterAgentExecution"
    BEFORE_TOOL_EXECUTION = "BeforeToolExecution"
    AFTER_TOOL_EXECUTION = "AfterToolExecution"
    BEFORE_LLM_CALL = "BeforeLLMCall"
    AFTER_LLM_CALL = "AfterLLMCall"
    ON_ERROR = "OnError"

    def __str__(self) -> str:  # pragma: no cover - trivial
        return self.value


@dataclass(slots=True)
class HookContext:
    """Mutable payload passed through the handler chain for one event.

    Handlers may enrich :attr:`data`, replace :attr:`result`, or set
    :attr:`cancelled` to veto the pending operation. The emitting call site is
    responsible for honouring ``cancelled`` -- see
    :meth:`hooks.manager.HookManager.emit`.
    """

    event: HookEvent
    source: str
    data: dict[str, Any] = field(default_factory=dict)
    result: Any = None
    error: BaseException | None = None
    correlation_id: str = field(default_factory=lambda: uuid.uuid4().hex[:12])
    started_at: float = field(default_factory=time.perf_counter)
    cancelled: bool = False
    cancel_reason: str | None = None

    def cancel(self, reason: str) -> None:
        """Veto the pending operation."""
        self.cancelled = True
        self.cancel_reason = reason

    @property
    def elapsed_ms(self) -> float:
        return (time.perf_counter() - self.started_at) * 1000.0


__all__ = ["HookContext", "HookEvent"]

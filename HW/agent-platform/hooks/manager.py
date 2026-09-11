"""Registration and dispatch for platform hooks.

The manager is deliberately synchronous and dependency-free: hooks run on the
hot path of every agent, tool and LLM call, so they must be cheap and must never
take the process down. A handler that raises is logged and skipped; only an
explicit :meth:`HookContext.cancel` changes control flow.
"""

from __future__ import annotations

import logging
from collections import defaultdict
from contextlib import contextmanager
from dataclasses import dataclass
from typing import Any, Callable, Iterator, Protocol, runtime_checkable

from .events import HookContext, HookEvent

logger = logging.getLogger(__name__)

HookHandler = Callable[[HookContext], None]


@runtime_checkable
class HookProvider(Protocol):
    """Anything that can contribute handlers -- plugins implement this."""

    def hooks(self) -> dict[HookEvent, HookHandler]:  # pragma: no cover - protocol
        ...


@dataclass(slots=True, order=True)
class _Registration:
    priority: int
    order: int
    name: str
    handler: HookHandler


class HookManager:
    """Ordered, fail-soft hook dispatcher.

    Lower ``priority`` runs first; ties break on registration order so the
    chain is deterministic across processes.
    """

    def __init__(self) -> None:
        self._handlers: dict[HookEvent, list[_Registration]] = defaultdict(list)
        self._counter = 0

    # -- registration ----------------------------------------------------

    def register(
        self,
        event: HookEvent,
        handler: HookHandler,
        *,
        name: str | None = None,
        priority: int = 100,
    ) -> Callable[[], None]:
        """Register ``handler`` for ``event``; returns an unregister callable."""
        self._counter += 1
        registration = _Registration(
            priority=priority,
            order=self._counter,
            name=name or getattr(handler, "__name__", repr(handler)),
            handler=handler,
        )
        bucket = self._handlers[event]
        bucket.append(registration)
        bucket.sort()
        logger.debug("registered hook %s -> %s", event, registration.name)
        return lambda: self.unregister(event, registration.name)

    def register_provider(self, provider: HookProvider, *, priority: int = 100) -> None:
        """Register every handler exposed by a :class:`HookProvider`."""
        for event, handler in provider.hooks().items():
            self.register(
                event,
                handler,
                name=f"{type(provider).__name__}.{event.value}",
                priority=priority,
            )

    def unregister(self, event: HookEvent, name: str) -> bool:
        bucket = self._handlers.get(event, [])
        for index, registration in enumerate(bucket):
            if registration.name == name:
                bucket.pop(index)
                return True
        return False

    def clear(self, event: HookEvent | None = None) -> None:
        if event is None:
            self._handlers.clear()
        else:
            self._handlers.pop(event, None)

    def registered(self, event: HookEvent) -> list[str]:
        return [r.name for r in self._handlers.get(event, [])]

    # -- dispatch --------------------------------------------------------

    def emit(self, event: HookEvent, source: str, **data: Any) -> HookContext:
        """Build a context, run the chain, return the (possibly mutated) context."""
        return self.dispatch(HookContext(event=event, source=source, data=data))

    def dispatch(self, context: HookContext) -> HookContext:
        for registration in list(self._handlers.get(context.event, [])):
            try:
                registration.handler(context)
            except Exception:  # noqa: BLE001 - a bad hook must not kill the run
                logger.exception(
                    "hook %s failed for %s; continuing", registration.name, context.event
                )
            if context.cancelled:
                logger.warning(
                    "hook %s cancelled %s on %s: %s",
                    registration.name,
                    context.event,
                    context.source,
                    context.cancel_reason,
                )
                break
        return context

    def emit_error(self, source: str, error: BaseException, **data: Any) -> HookContext:
        context = HookContext(
            event=HookEvent.ON_ERROR, source=source, data=data, error=error
        )
        return self.dispatch(context)

    # -- convenience -----------------------------------------------------

    @contextmanager
    def around(
        self,
        before: HookEvent,
        after: HookEvent,
        source: str,
        **data: Any,
    ) -> Iterator[HookContext]:
        """Emit ``before``/``after`` around a block, and ``OnError`` on failure.

        The yielded context is shared by both events, so a *before* handler can
        stash state that the *after* handler reads. Callers must check
        ``ctx.cancelled`` after entering the block.
        """
        context = self.emit(before, source, **data)
        try:
            yield context
        except Exception as exc:
            context.error = exc
            self.emit_error(source, exc, **data)
            raise
        finally:
            after_context = HookContext(
                event=after,
                source=source,
                data=context.data,
                result=context.result,
                error=context.error,
                correlation_id=context.correlation_id,
                started_at=context.started_at,
            )
            self.dispatch(after_context)
            context.result = after_context.result


__all__ = ["HookHandler", "HookManager", "HookProvider"]

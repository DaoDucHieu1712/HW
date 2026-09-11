"""Short-term memory: the bounded conversation buffer for one run.

Bounded on purpose. An unbounded transcript is the most common way an agent
platform quietly runs out of context halfway through a workflow; dropping the
oldest turns is a predictable failure mode, an overflowing window is not.
"""

from __future__ import annotations

from collections import deque
from typing import Any, Iterable

from llms.base import LLMMessage

from .base import Memory, MemoryRecord, keyword_score


class ShortTermMemory(Memory):
    """FIFO window over recent turns and observations."""

    def __init__(self, max_items: int = 40) -> None:
        self.max_items = max_items
        self._records: deque[MemoryRecord] = deque(maxlen=max_items)

    # -- Memory ----------------------------------------------------------

    def add(self, record: MemoryRecord) -> MemoryRecord:
        self._records.append(record)
        return record

    def search(self, query: str, *, limit: int = 5, **filters: Any) -> list[MemoryRecord]:
        kind = filters.get("kind")
        scored: list[MemoryRecord] = []
        for record in self._records:
            if kind and record.kind != kind:
                continue
            score = keyword_score(query, record.content)
            if score > 0:
                record.score = score
                scored.append(record)
        scored.sort(key=lambda item: (item.score or 0.0, item.created_at), reverse=True)
        return scored[:limit]

    def all(self) -> list[MemoryRecord]:
        return list(self._records)

    def clear(self) -> None:
        self._records.clear()

    # -- conversation helpers -------------------------------------------

    def remember_turn(self, role: str, content: str, **metadata: Any) -> MemoryRecord:
        """Record one conversation turn."""
        return self.add(
            MemoryRecord(content=content, kind=f"turn:{role}", metadata=metadata)
        )

    def remember_observation(self, source: str, content: str, **metadata: Any) -> MemoryRecord:
        """Record a tool result or other non-conversational fact."""
        return self.add(
            MemoryRecord(content=content, kind="observation", metadata={"source": source, **metadata})
        )

    def history(self, *, limit: int | None = None) -> list[LLMMessage]:
        """Replay the buffered turns as LLM messages, oldest first."""
        turns = [record for record in self._records if record.kind.startswith("turn:")]
        if limit:
            turns = turns[-limit:]
        messages: list[LLMMessage] = []
        for record in turns:
            role = record.kind.split(":", 1)[1]
            messages.append(
                LLMMessage(role="assistant" if role == "assistant" else "user", content=record.content)
            )
        return messages

    def transcript(self, *, limit: int = 10) -> str:
        """Human-readable tail of the buffer, for prompts and debugging."""
        recent: Iterable[MemoryRecord] = list(self._records)[-limit:]
        return "\n".join(f"[{record.kind}] {record.content.strip()[:500]}" for record in recent)

    def __len__(self) -> int:
        return len(self._records)


__all__ = ["ShortTermMemory"]

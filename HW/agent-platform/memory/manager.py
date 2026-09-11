"""Facade over the three memory layers.

Agents hold a :class:`MemoryManager`, not three stores. The manager decides
which layer a piece of information belongs in and assembles the single
"what you already know" block that goes into a prompt.
"""

from __future__ import annotations

import logging
from typing import Any

from configs.settings import MemorySettings, PlatformSettings

from .base import MemoryRecord
from .long_term import LongTermMemory
from .short_term import ShortTermMemory
from .vector import VectorMemory

logger = logging.getLogger(__name__)


class MemoryManager:
    """Coordinates short-term, long-term and vector memory."""

    def __init__(
        self,
        short_term: ShortTermMemory,
        long_term: LongTermMemory,
        vector: VectorMemory,
    ) -> None:
        self.short_term = short_term
        self.long_term = long_term
        self.vector = vector

    # -- construction ----------------------------------------------------

    @classmethod
    def from_settings(cls, settings: PlatformSettings) -> "MemoryManager":
        memory: MemorySettings = settings.memory
        return cls(
            short_term=ShortTermMemory(max_items=memory.short_term_max_messages),
            long_term=LongTermMemory(settings.resolve(memory.long_term_path)),
            vector=VectorMemory(
                path=str(settings.resolve(memory.vector_path)),
                collection=memory.vector_collection,
                backend=memory.vector_backend,
                top_k=memory.top_k,
            ),
        )

    # -- writing ---------------------------------------------------------

    def observe(self, source: str, content: str, **metadata: Any) -> MemoryRecord:
        """Record something that happened during this run (short term only)."""
        return self.short_term.remember_observation(source, content, **metadata)

    def remember_turn(self, role: str, content: str) -> MemoryRecord:
        return self.short_term.remember_turn(role, content)

    def learn(self, content: str, *, kind: str = "fact", index: bool = True, **metadata: Any) -> MemoryRecord:
        """Persist a durable fact, and index it for retrieval.

        Use this for knowledge that outlives the run: an architectural decision,
        a module's ownership, the reason a workaround exists.
        """
        record = self.long_term.remember(content, kind=kind, **metadata)
        if index:
            self.vector.add(record)
        return record

    def index_document(self, content: str, *, source: str, **metadata: Any) -> MemoryRecord:
        """Add a document chunk to the retrieval corpus without asserting it as a fact."""
        return self.vector.add(
            MemoryRecord(content=content, kind="document", metadata={"source": source, **metadata})
        )

    # -- reading ---------------------------------------------------------

    def recall(self, query: str, *, limit: int = 5) -> list[MemoryRecord]:
        """Retrieve across long-term and vector memory, best first, de-duplicated."""
        results = self.vector.search(query, limit=limit) + self.long_term.search(query, limit=limit)
        unique: dict[str, MemoryRecord] = {}
        for record in results:
            key = record.content.strip()[:200]
            if key not in unique or (record.score or 0) > (unique[key].score or 0):
                unique[key] = record
        ordered = sorted(unique.values(), key=lambda item: item.score or 0.0, reverse=True)
        return ordered[:limit]

    def context_block(self, query: str, *, limit: int = 5, recent: int = 6) -> str:
        """Assemble the memory section of a prompt.

        Returns an empty string when there is nothing worth including -- an empty
        "Known context" heading is noise that costs tokens and teaches nothing.
        """
        sections: list[str] = []

        recalled = self.recall(query, limit=limit)
        if recalled:
            sections.append(
                "## Known project context\n"
                + "\n".join(f"- {record.content.strip()}" for record in recalled)
            )

        transcript = self.short_term.transcript(limit=recent)
        if transcript.strip():
            sections.append("## Recent activity in this run\n" + transcript)

        return "\n\n".join(sections)

    def reset_run(self) -> None:
        """Clear per-run state, keeping durable knowledge."""
        self.short_term.clear()

    def stats(self) -> dict[str, Any]:
        return {
            "short_term": len(self.short_term),
            "long_term": len(self.long_term),
            "vector_backend": self.vector.backend,
        }


__all__ = ["MemoryManager"]

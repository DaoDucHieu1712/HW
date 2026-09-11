"""Memory contracts shared by the three layers.

The layers answer different questions:

* short term -- "what did we just say?" (conversation, bounded, volatile)
* long term  -- "what do we know about this project?" (durable facts, keyed)
* vector     -- "what is *relevant* to this?" (semantic retrieval over corpus)
"""

from __future__ import annotations

import abc
import time
import uuid
from dataclasses import dataclass, field
from typing import Any, Iterable, Sequence


@dataclass(slots=True)
class MemoryRecord:
    """One unit of remembered information."""

    content: str
    kind: str = "note"
    id: str = field(default_factory=lambda: uuid.uuid4().hex)
    metadata: dict[str, Any] = field(default_factory=dict)
    created_at: float = field(default_factory=time.time)
    score: float | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "kind": self.kind,
            "content": self.content,
            "metadata": self.metadata,
            "created_at": self.created_at,
            "score": self.score,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "MemoryRecord":
        return cls(
            id=data.get("id", uuid.uuid4().hex),
            kind=data.get("kind", "note"),
            content=data.get("content", ""),
            metadata=data.get("metadata", {}) or {},
            created_at=float(data.get("created_at", time.time())),
            score=data.get("score"),
        )


class Memory(abc.ABC):
    """Minimal store interface every layer implements."""

    @abc.abstractmethod
    def add(self, record: MemoryRecord) -> MemoryRecord:
        """Persist one record and return it (with any assigned id)."""

    @abc.abstractmethod
    def search(self, query: str, *, limit: int = 5, **filters: Any) -> list[MemoryRecord]:
        """Return the records most relevant to ``query``."""

    @abc.abstractmethod
    def all(self) -> list[MemoryRecord]:
        """Every record currently held."""

    @abc.abstractmethod
    def clear(self) -> None:
        """Drop everything. Used between runs and in tests."""

    def add_many(self, records: Iterable[MemoryRecord]) -> list[MemoryRecord]:
        return [self.add(record) for record in records]

    def render(self, records: Sequence[MemoryRecord], *, header: str = "") -> str:
        """Format records as a prompt block."""
        if not records:
            return ""
        lines = [header] if header else []
        for record in records:
            prefix = f"- [{record.kind}]"
            lines.append(f"{prefix} {record.content.strip()}")
        return "\n".join(lines)


def keyword_score(query: str, text: str) -> float:
    """Cheap lexical relevance in [0, 1].

    Used by the in-memory vector fallback so retrieval still returns something
    sensible when no embedding backend is installed.
    """
    query_terms = {term for term in _tokenise(query) if len(term) > 2}
    if not query_terms:
        return 0.0
    text_terms = set(_tokenise(text))
    if not text_terms:
        return 0.0
    overlap = query_terms & text_terms
    return len(overlap) / len(query_terms)


def _tokenise(text: str) -> list[str]:
    return [token for token in "".join(
        char.lower() if char.isalnum() else " " for char in text
    ).split() if token]


__all__ = ["Memory", "MemoryRecord", "keyword_score"]

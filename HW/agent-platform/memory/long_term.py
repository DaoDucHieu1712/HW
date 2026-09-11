"""Long-term memory: durable project knowledge on disk.

Backed by a single JSON file. That is a deliberate choice for the default
deployment -- it is diffable, greppable, and survivable without a service. Swap
in a database-backed :class:`memory.base.Memory` when the corpus outgrows it.
"""

from __future__ import annotations

import json
import logging
import threading
from pathlib import Path
from typing import Any

from .base import Memory, MemoryRecord, keyword_score

logger = logging.getLogger(__name__)


class LongTermMemory(Memory):
    """Append-and-upsert store of facts about the project."""

    def __init__(self, path: str | Path, *, autosave: bool = True) -> None:
        self.path = Path(path)
        self.autosave = autosave
        self._lock = threading.Lock()
        self._records: dict[str, MemoryRecord] = {}
        self.load()

    # -- persistence -----------------------------------------------------

    def load(self) -> None:
        if not self.path.exists():
            return
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            logger.warning("could not read long-term memory at %s: %s", self.path, exc)
            return
        self._records = {
            record["id"]: MemoryRecord.from_dict(record)
            for record in payload.get("records", [])
            if record.get("id")
        }
        logger.debug("loaded %d long-term records", len(self._records))

    def save(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        payload = {"version": 1, "records": [r.to_dict() for r in self._records.values()]}
        temporary = self.path.with_suffix(self.path.suffix + ".tmp")
        temporary.write_text(json.dumps(payload, indent=2, default=str), encoding="utf-8")
        temporary.replace(self.path)

    # -- Memory ----------------------------------------------------------

    def add(self, record: MemoryRecord) -> MemoryRecord:
        with self._lock:
            self._records[record.id] = record
            if self.autosave:
                self.save()
        return record

    def remember(self, content: str, *, kind: str = "fact", **metadata: Any) -> MemoryRecord:
        """Convenience wrapper that de-duplicates identical content."""
        existing = next(
            (r for r in self._records.values() if r.kind == kind and r.content == content),
            None,
        )
        if existing is not None:
            existing.metadata.update(metadata)
            return existing
        return self.add(MemoryRecord(content=content, kind=kind, metadata=metadata))

    def search(self, query: str, *, limit: int = 5, **filters: Any) -> list[MemoryRecord]:
        kind = filters.get("kind")
        results: list[MemoryRecord] = []
        for record in self._records.values():
            if kind and record.kind != kind:
                continue
            score = keyword_score(query, record.content + " " + json.dumps(record.metadata, default=str))
            if score > 0:
                record.score = score
                results.append(record)
        results.sort(key=lambda item: (item.score or 0.0, item.created_at), reverse=True)
        return results[:limit]

    def all(self) -> list[MemoryRecord]:
        return sorted(self._records.values(), key=lambda record: record.created_at)

    def clear(self) -> None:
        with self._lock:
            self._records.clear()
            if self.autosave:
                self.save()

    def forget(self, record_id: str) -> bool:
        """Delete one record. Memories that turn out to be wrong must be removable."""
        with self._lock:
            removed = self._records.pop(record_id, None) is not None
            if removed and self.autosave:
                self.save()
        return removed

    def __len__(self) -> int:
        return len(self._records)


__all__ = ["LongTermMemory"]

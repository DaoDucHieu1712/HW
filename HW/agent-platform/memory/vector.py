"""Vector memory: semantic retrieval over the project corpus.

ChromaDB is the configured backend. When it is not installed -- or the store
cannot be opened -- the class falls back to an in-process lexical index so
retrieval degrades in quality rather than failing the run outright. The active
backend is reported by :attr:`VectorMemory.backend`, so callers never have to
guess which one answered.
"""

from __future__ import annotations

import logging
from typing import Any

from .base import Memory, MemoryRecord, keyword_score

logger = logging.getLogger(__name__)


class VectorMemory(Memory):
    """Embedding-backed store with a lexical fallback."""

    def __init__(
        self,
        *,
        path: str,
        collection: str = "project-knowledge",
        backend: str = "chroma",
        top_k: int = 5,
    ) -> None:
        self.path = path
        self.collection_name = collection
        self.top_k = top_k
        self._fallback: list[MemoryRecord] = []
        self._collection: Any | None = None
        self.backend = "memory"

        if backend == "chroma":
            self._collection = self._open_chroma(path, collection)
            self.backend = "chroma" if self._collection is not None else "memory"
        if self.backend == "memory":
            logger.info("vector memory running on the in-process lexical index")

    @staticmethod
    def _open_chroma(path: str, collection: str) -> Any | None:
        try:
            import chromadb
        except ImportError:
            logger.info("chromadb is not installed; using the lexical fallback")
            return None
        try:
            client = chromadb.PersistentClient(path=path)
            return client.get_or_create_collection(
                name=collection, metadata={"hnsw:space": "cosine"}
            )
        except Exception as exc:  # noqa: BLE001 - a broken store must not stop a run
            logger.warning("could not open the chroma collection at %s: %s", path, exc)
            return None

    # -- Memory ----------------------------------------------------------

    def add(self, record: MemoryRecord) -> MemoryRecord:
        if self._collection is not None:
            try:
                self._collection.upsert(
                    ids=[record.id],
                    documents=[record.content],
                    metadatas=[self._flatten(record)],
                )
                return record
            except Exception as exc:  # noqa: BLE001
                logger.warning("chroma upsert failed, falling back to memory: %s", exc)
                self._collection = None
                self.backend = "memory"
        self._fallback.append(record)
        return record

    def search(self, query: str, *, limit: int = 5, **filters: Any) -> list[MemoryRecord]:
        count = limit or self.top_k
        if self._collection is not None:
            try:
                return self._search_chroma(query, count, filters)
            except Exception as exc:  # noqa: BLE001
                logger.warning("chroma query failed, falling back to memory: %s", exc)
                self._collection = None
                self.backend = "memory"

        scored: list[MemoryRecord] = []
        for record in self._fallback:
            if not _matches(record, filters):
                continue
            score = keyword_score(query, record.content)
            if score > 0:
                record.score = score
                scored.append(record)
        scored.sort(key=lambda item: item.score or 0.0, reverse=True)
        return scored[:count]

    def _search_chroma(self, query: str, limit: int, filters: dict[str, Any]) -> list[MemoryRecord]:
        assert self._collection is not None
        where = {key: value for key, value in filters.items() if value is not None} or None
        response = self._collection.query(query_texts=[query], n_results=limit, where=where)

        documents = (response.get("documents") or [[]])[0]
        metadatas = (response.get("metadatas") or [[]])[0]
        ids = (response.get("ids") or [[]])[0]
        distances = (response.get("distances") or [[]])[0]

        results: list[MemoryRecord] = []
        for index, document in enumerate(documents):
            metadata = dict(metadatas[index] or {}) if index < len(metadatas) else {}
            distance = distances[index] if index < len(distances) else None
            results.append(
                MemoryRecord(
                    id=ids[index] if index < len(ids) else "",
                    content=document,
                    kind=str(metadata.pop("kind", "note")),
                    metadata=metadata,
                    # Cosine distance -> similarity, so higher is always better.
                    score=None if distance is None else max(0.0, 1.0 - float(distance)),
                )
            )
        return results

    def all(self) -> list[MemoryRecord]:
        if self._collection is None:
            return list(self._fallback)
        try:
            payload = self._collection.get()
        except Exception:  # noqa: BLE001 - pragma: no cover
            return list(self._fallback)
        return [
            MemoryRecord(
                id=payload["ids"][index],
                content=document,
                kind=str((payload.get("metadatas") or [{}])[index].get("kind", "note")),
                metadata=dict((payload.get("metadatas") or [{}])[index] or {}),
            )
            for index, document in enumerate(payload.get("documents") or [])
        ]

    def clear(self) -> None:
        self._fallback.clear()
        if self._collection is not None:
            try:
                ids = (self._collection.get() or {}).get("ids") or []
                if ids:
                    self._collection.delete(ids=ids)
            except Exception as exc:  # noqa: BLE001 - pragma: no cover
                logger.warning("could not clear the chroma collection: %s", exc)

    # -- helpers ---------------------------------------------------------

    @staticmethod
    def _flatten(record: MemoryRecord) -> dict[str, Any]:
        """Chroma metadata accepts scalars only."""
        flat: dict[str, Any] = {"kind": record.kind, "created_at": record.created_at}
        for key, value in record.metadata.items():
            flat[key] = value if isinstance(value, (str, int, float, bool)) else str(value)
        return flat


def _matches(record: MemoryRecord, filters: dict[str, Any]) -> bool:
    for key, value in filters.items():
        if value is None:
            continue
        actual = record.kind if key == "kind" else record.metadata.get(key)
        if actual != value:
            return False
    return True


__all__ = ["VectorMemory"]

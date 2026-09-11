"""Checkpointer construction.

Checkpointing is what makes human-in-the-loop and resume-after-crash possible:
the graph persists state between nodes, so a run interrupted for review can be
picked up on the same thread id. When LangGraph is not installed the platform
runs without checkpointing rather than refusing to start.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Any

from configs.settings import GraphSettings

logger = logging.getLogger(__name__)


def build_checkpointer(settings: GraphSettings) -> Any | None:
    """Return a LangGraph checkpointer, or ``None`` when unavailable."""
    backend = (settings.checkpoint_backend or "none").lower()
    if backend == "none":
        return None

    if backend == "sqlite":
        checkpointer = _sqlite_checkpointer(settings.checkpoint_path)
        if checkpointer is not None:
            return checkpointer
        logger.warning("falling back to the in-memory checkpointer")

    try:
        from langgraph.checkpoint.memory import MemorySaver
    except ImportError:
        logger.info("langgraph is not installed; running without checkpoints")
        return None
    return MemorySaver()


def _sqlite_checkpointer(path: str) -> Any | None:
    try:
        import sqlite3

        from langgraph.checkpoint.sqlite import SqliteSaver
    except ImportError:
        logger.info(
            "langgraph-checkpoint-sqlite is not installed; sqlite checkpointing unavailable"
        )
        return None

    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    # check_same_thread=False: the graph may resume a thread from another worker.
    connection = sqlite3.connect(str(target), check_same_thread=False)
    logger.info("checkpointing to %s", target)
    return SqliteSaver(connection)


def thread_config(correlation_id: str, **extra: Any) -> dict[str, Any]:
    """Build the ``config`` argument that scopes a run to one thread."""
    return {"configurable": {"thread_id": correlation_id, **extra}}


__all__ = ["build_checkpointer", "thread_config"]

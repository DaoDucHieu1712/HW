"""Seed vector memory from the codebase.

Retrieval is only as good as what was indexed. This walks the workspace, splits
each source file into overlapping windows, and stores them so the planner and
review agents can recall relevant code without searching from scratch.

    python scripts/index_codebase.py --glob "*.cs" --glob "*.md"
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from configs.settings import load_settings  # noqa: E402
from configs.telemetry import configure_logging  # noqa: E402
from memory.manager import MemoryManager  # noqa: E402
from tools.workspace import Workspace  # noqa: E402


def chunk(text: str, *, size: int = 60, overlap: int = 10) -> list[str]:
    """Split into overlapping line windows.

    The overlap matters: a class declaration and the method that needs it often
    land either side of a boundary, and a window that starts mid-method
    retrieves badly.
    """
    lines = text.splitlines()
    if len(lines) <= size:
        return ["\n".join(lines)] if lines else []
    step = max(1, size - overlap)
    return [
        "\n".join(lines[start : start + size])
        for start in range(0, len(lines), step)
        if lines[start : start + size]
    ]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Index the workspace into vector memory.")
    parser.add_argument("--glob", action="append", dest="globs", help="file pattern to index")
    parser.add_argument("--chunk-lines", default=60, type=int)
    parser.add_argument("--limit", default=None, type=int, help="stop after this many files")
    parser.add_argument("--clear", action="store_true", help="empty the store first")
    args = parser.parse_args(argv)

    settings = load_settings()
    configure_logging(settings.observability)

    workspace = Workspace(settings.workspace)
    memory = MemoryManager.from_settings(settings)
    if args.clear:
        memory.vector.clear()

    patterns = tuple(args.globs or ("*.cs", "*.py", "*.ts", "*.sql", "*.md"))
    files = indexed = chunks = 0

    for path in workspace.iter_files(patterns):
        if args.limit and files >= args.limit:
            break
        files += 1
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        relative = workspace.relative(path)
        for number, body in enumerate(chunk(text, size=args.chunk_lines)):
            memory.index_document(body, source=relative, chunk=number, language=path.suffix)
            chunks += 1
        indexed += 1

    print(
        f"indexed {chunks} chunk(s) from {indexed}/{files} file(s) "
        f"into the {memory.vector.backend} backend"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

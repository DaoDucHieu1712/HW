"""Prompt template loading and rendering.

Prompts are Markdown files on disk, not string literals in Python. That keeps
them reviewable as prose, lets a non-engineer edit them, and makes a prompt
change show up in a diff as a prompt change.

Placeholders use ``{{ name }}``. A missing placeholder renders as an empty
string rather than raising -- a half-filled prompt is recoverable, a crash in
the middle of a workflow is not.
"""

from __future__ import annotations

import logging
import re
from pathlib import Path
from typing import Any, Iterator

logger = logging.getLogger(__name__)

_PLACEHOLDER = re.compile(r"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}")


class PromptLibrary:
    """Lazy, cached view over a directory of Markdown prompt templates."""

    def __init__(self, directory: str | Path) -> None:
        self.directory = Path(directory)
        self._cache: dict[str, str] = {}

    def path_for(self, name: str) -> Path:
        return self.directory / f"{name}.md"

    def has(self, name: str) -> bool:
        return name in self._cache or self.path_for(name).exists()

    def raw(self, name: str) -> str:
        """Template text, read once and cached."""
        if name not in self._cache:
            path = self.path_for(name)
            if not path.exists():
                raise KeyError(f"prompt template not found: {path}")
            self._cache[name] = path.read_text(encoding="utf-8")
        return self._cache[name]

    def render(self, name: str, **values: Any) -> str:
        """Render a template, substituting ``{{ placeholders }}``."""
        return self.substitute(self.raw(name), **values)

    @staticmethod
    def substitute(template: str, **values: Any) -> str:
        used: set[str] = set()

        def replace(match: re.Match[str]) -> str:
            key = match.group(1)
            used.add(key)
            value = values.get(key)
            return "" if value is None else str(value)

        rendered = _PLACEHOLDER.sub(replace, template)
        missing = used - set(values)
        if missing:
            logger.debug("prompt placeholders left empty: %s", ", ".join(sorted(missing)))
        return _collapse_blank_lines(rendered)

    def names(self) -> list[str]:
        if not self.directory.is_dir():
            return []
        return sorted(path.stem for path in self.directory.glob("*.md"))

    def __iter__(self) -> Iterator[str]:
        return iter(self.names())


def _collapse_blank_lines(text: str) -> str:
    """Squash the runs of blank lines an empty placeholder leaves behind."""
    return re.sub(r"\n{3,}", "\n\n", text).strip() + "\n"


__all__ = ["PromptLibrary"]

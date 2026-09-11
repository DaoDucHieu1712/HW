"""Filesystem tools: ``read_file``, ``write_file``, ``search_code``.

Each one takes a :class:`tools.workspace.Workspace` and nothing else, so a test
can point it at a temporary directory and assert on real behaviour.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any

from .base import Tool, ToolError, ToolResult
from .workspace import Workspace, WorkspaceError

#: Extensions searched by default -- source, not build output or binaries.
DEFAULT_CODE_GLOBS = (
    "*.cs",
    "*.py",
    "*.ts",
    "*.tsx",
    "*.js",
    "*.sql",
    "*.json",
    "*.yaml",
    "*.yml",
    "*.md",
    "*.csproj",
    "*.slnx",
)


class ReadFileTool(Tool):
    name = "read_file"
    description = (
        "Read a UTF-8 text file from the workspace. Supports reading a line range "
        "so a large file can be inspected without pulling all of it into context. "
        "Returns the file content with 1-based line numbers."
    )
    read_only = True
    tags = ("filesystem", "read")
    input_schema = {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "Workspace-relative path, e.g. HW.Domain/Orders/Order.cs",
            },
            "start_line": {
                "type": "integer",
                "description": "1-based first line to return. Omit to start at the top.",
            },
            "end_line": {
                "type": "integer",
                "description": "1-based last line to return, inclusive.",
            },
        },
        "required": ["path"],
    }

    def __init__(self, workspace: Workspace) -> None:
        self.workspace = workspace

    def _execute(
        self, path: str, start_line: int | None = None, end_line: int | None = None
    ) -> ToolResult:
        try:
            content = self.workspace.read_text(path)
        except WorkspaceError as exc:
            raise ToolError(str(exc)) from exc

        lines = content.splitlines()
        first = max(1, start_line or 1)
        last = min(len(lines), end_line or len(lines))
        if first > len(lines):
            raise ToolError(f"{path} has {len(lines)} lines; start_line={first} is past the end")

        window = lines[first - 1 : last]
        numbered = "\n".join(f"{first + i:>5}| {line}" for i, line in enumerate(window))
        return ToolResult.success(
            self.name,
            numbered,
            path=path,
            total_lines=len(lines),
            returned_lines=len(window),
        )


class WriteFileTool(Tool):
    name = "write_file"
    description = (
        "Create or overwrite a text file in the workspace. Writes the whole file, "
        "so include the complete intended content. Parent directories are created. "
        "Refuses to run when the platform is in read-only mode."
    )
    read_only = False
    tags = ("filesystem", "write")
    input_schema = {
        "type": "object",
        "properties": {
            "path": {"type": "string", "description": "Workspace-relative path to write."},
            "content": {"type": "string", "description": "Complete new file content."},
        },
        "required": ["path", "content"],
    }

    def __init__(self, workspace: Workspace) -> None:
        self.workspace = workspace

    def _execute(self, path: str, content: str) -> ToolResult:
        try:
            existed = self.workspace.resolve(path).exists()
            written = self.workspace.write_text(path, content)
        except WorkspaceError as exc:
            raise ToolError(str(exc)) from exc

        return ToolResult.success(
            self.name,
            f"{'updated' if existed else 'created'} {self.workspace.relative(written)} "
            f"({len(content.splitlines())} lines)",
            path=self.workspace.relative(written),
            created=not existed,
            bytes_written=len(content.encode("utf-8")),
        )


@dataclass(slots=True)
class CodeMatch:
    path: str
    line: int
    text: str

    def to_dict(self) -> dict[str, Any]:
        return {"path": self.path, "line": self.line, "text": self.text}


class SearchCodeTool(Tool):
    name = "search_code"
    description = (
        "Search the workspace source tree with a regular expression and return "
        "matching lines with their file path and line number. Use this to locate "
        "a symbol, a call site, or an error message before reading whole files."
    )
    read_only = True
    tags = ("filesystem", "read", "search")
    input_schema = {
        "type": "object",
        "properties": {
            "pattern": {
                "type": "string",
                "description": "Python regular expression, e.g. class\\s+OrderService",
            },
            "file_glob": {
                "type": "string",
                "description": "Filename glob to restrict the search, e.g. *.cs",
            },
            "max_results": {
                "type": "integer",
                "description": "Maximum matches to return (default 50).",
            },
            "ignore_case": {"type": "boolean", "description": "Case-insensitive match."},
        },
        "required": ["pattern"],
    }

    def __init__(self, workspace: Workspace, *, default_max_results: int = 50) -> None:
        self.workspace = workspace
        self.default_max_results = default_max_results

    def _execute(
        self,
        pattern: str,
        file_glob: str | None = None,
        max_results: int | None = None,
        ignore_case: bool | None = None,
    ) -> ToolResult:
        try:
            regex = re.compile(pattern, re.IGNORECASE if ignore_case else 0)
        except re.error as exc:
            raise ToolError(f"invalid regular expression: {exc}") from exc

        limit = max_results or self.default_max_results
        globs = (file_glob,) if file_glob else DEFAULT_CODE_GLOBS
        matches: list[CodeMatch] = []

        for path in self.workspace.iter_files(globs):
            try:
                text = path.read_text(encoding="utf-8", errors="ignore")
            except OSError:
                continue
            for number, line in enumerate(text.splitlines(), start=1):
                if regex.search(line):
                    matches.append(
                        CodeMatch(
                            path=self.workspace.relative(path),
                            line=number,
                            text=line.strip()[:300],
                        )
                    )
                    if len(matches) >= limit:
                        break
            if len(matches) >= limit:
                break

        return ToolResult.success(
            self.name,
            [match.to_dict() for match in matches],
            pattern=pattern,
            truncated=len(matches) >= limit,
        )


__all__ = [
    "CodeMatch",
    "DEFAULT_CODE_GLOBS",
    "ReadFileTool",
    "SearchCodeTool",
    "WriteFileTool",
]

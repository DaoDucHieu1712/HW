"""Path sandbox and command runner shared by the filesystem and build tools.

Every path a model supplies passes through :meth:`Workspace.resolve`, which is
the deterministic boundary: it resolves symlinks, rejects anything outside the
configured root, and refuses denied globs. A prompt cannot argue its way past
it, because it never reads the prompt.
"""

from __future__ import annotations

import fnmatch
import logging
import os
import shlex
import subprocess
from dataclasses import dataclass
from pathlib import Path, PurePath
from typing import Iterable, Sequence

from configs.settings import WorkspaceSettings

logger = logging.getLogger(__name__)


class WorkspaceError(RuntimeError):
    """Raised when a path or command violates workspace policy."""


@dataclass(slots=True)
class CommandOutcome:
    """Result of running a build/test command."""

    command: str
    exit_code: int
    stdout: str
    stderr: str
    timed_out: bool = False

    @property
    def ok(self) -> bool:
        return self.exit_code == 0 and not self.timed_out

    def tail(self, lines: int = 60) -> str:
        """The last N lines of combined output -- where failures live."""
        combined = (self.stdout + "\n" + self.stderr).strip().splitlines()
        return "\n".join(combined[-lines:])

    def to_dict(self) -> dict[str, object]:
        return {
            "command": self.command,
            "exit_code": self.exit_code,
            "ok": self.ok,
            "timed_out": self.timed_out,
            "output_tail": self.tail(),
        }


#: Directories never walked when searching -- they are large and never source.
SKIP_DIRS = {
    ".git",
    ".vs",
    ".idea",
    "node_modules",
    "bin",
    "obj",
    "dist",
    "build",
    "__pycache__",
    ".venv",
    "venv",
    ".state",
}


class Workspace:
    """Guarded view over the source tree the platform is allowed to touch."""

    def __init__(self, settings: WorkspaceSettings) -> None:
        self.settings = settings
        self.root = Path(settings.root).expanduser().resolve()
        if not self.root.exists():
            raise WorkspaceError(f"workspace root does not exist: {self.root}")

    # -- paths -----------------------------------------------------------

    def resolve(self, relative: str, *, must_exist: bool = False) -> Path:
        """Resolve a caller-supplied path inside the workspace, or raise."""
        if not relative or not str(relative).strip():
            raise WorkspaceError("path must not be empty")
        candidate = Path(relative).expanduser()
        absolute = (candidate if candidate.is_absolute() else self.root / candidate).resolve()

        try:
            absolute.relative_to(self.root)
        except ValueError as exc:
            raise WorkspaceError(
                f"path escapes the workspace root: {relative}"
            ) from exc

        if self.is_denied(absolute):
            raise WorkspaceError(f"path is denied by policy: {relative}")
        if must_exist and not absolute.exists():
            raise WorkspaceError(f"path does not exist: {relative}")
        return absolute

    def is_denied(self, path: Path) -> bool:
        relative = self.relative(path)
        posix = PurePath(relative).as_posix()
        return any(
            fnmatch.fnmatch(posix, pattern.lstrip("/"))
            or fnmatch.fnmatch("/" + posix, pattern)
            for pattern in self.settings.denied_globs
        )

    def relative(self, path: Path) -> str:
        try:
            return path.resolve().relative_to(self.root).as_posix()
        except ValueError:
            return path.as_posix()

    # -- io --------------------------------------------------------------

    def read_text(self, relative: str, *, max_bytes: int | None = None) -> str:
        path = self.resolve(relative, must_exist=True)
        if path.is_dir():
            raise WorkspaceError(f"{relative} is a directory")
        limit = max_bytes or self.settings.max_file_bytes
        size = path.stat().st_size
        if size > limit:
            raise WorkspaceError(
                f"{relative} is {size} bytes, over the {limit} byte limit; read a narrower range"
            )
        return path.read_text(encoding="utf-8", errors="replace")

    def write_text(self, relative: str, content: str, *, create_dirs: bool = True) -> Path:
        if not self.settings.allow_writes:
            raise WorkspaceError("workspace is read-only (workspace.allow_writes is false)")
        path = self.resolve(relative)
        if create_dirs:
            path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")
        return path

    def iter_files(self, patterns: Sequence[str] = ("*",)) -> Iterable[Path]:
        """Walk the tree, skipping build output and VCS metadata."""
        for current, directories, filenames in os.walk(self.root):
            directories[:] = [d for d in directories if d not in SKIP_DIRS]
            for filename in filenames:
                if not any(fnmatch.fnmatch(filename, pattern) for pattern in patterns):
                    continue
                path = Path(current) / filename
                if not self.is_denied(path):
                    yield path

    # -- commands --------------------------------------------------------

    def run(
        self,
        command: str,
        *,
        cwd: str | None = None,
        timeout: float = 900.0,
        env: dict[str, str] | None = None,
    ) -> CommandOutcome:
        """Run a shell command inside the workspace.

        The command is split with :func:`shlex.split` and executed without a
        shell, so caller-supplied text cannot chain a second command.
        """
        working_dir = self.resolve(cwd, must_exist=True) if cwd else self.root
        argv = shlex.split(command, posix=os.name != "nt")
        if not argv:
            raise WorkspaceError("empty command")

        logger.info("running: %s (cwd=%s)", command, self.relative(working_dir))
        try:
            completed = subprocess.run(  # noqa: S603 - argv is not shell-interpreted
                argv,
                cwd=working_dir,
                capture_output=True,
                text=True,
                timeout=timeout,
                env={**os.environ, **(env or {})},
                check=False,
            )
        except FileNotFoundError as exc:
            raise WorkspaceError(f"command not found: {argv[0]}") from exc
        except subprocess.TimeoutExpired as exc:
            return CommandOutcome(
                command=command,
                exit_code=-1,
                stdout=(exc.stdout or "") if isinstance(exc.stdout, str) else "",
                stderr=f"timed out after {timeout}s",
                timed_out=True,
            )
        return CommandOutcome(
            command=command,
            exit_code=completed.returncode,
            stdout=completed.stdout or "",
            stderr=completed.stderr or "",
        )


__all__ = ["CommandOutcome", "SKIP_DIRS", "Workspace", "WorkspaceError"]

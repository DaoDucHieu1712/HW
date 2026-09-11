"""Shared fixtures.

Everything here builds a platform that runs entirely offline: an ``EchoLLM``
instead of Claude, a temporary directory instead of the real repository, and no
MCP servers. Tests that need a real collaborator construct it explicitly.
"""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

import pytest

# The platform is a flat package set rooted at the repository directory.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from configs.settings import PlatformSettings  # noqa: E402
from container import Container, build_container  # noqa: E402
from hooks.manager import HookManager  # noqa: E402
from llms.fake import EchoLLM  # noqa: E402
from memory.manager import MemoryManager  # noqa: E402
from memory.long_term import LongTermMemory  # noqa: E402
from memory.short_term import ShortTermMemory  # noqa: E402
from memory.vector import VectorMemory  # noqa: E402
from tools.workspace import Workspace  # noqa: E402


@pytest.fixture
def workspace_root(tmp_path: Path) -> Path:
    """A small source tree to run tools against."""
    (tmp_path / "src").mkdir()
    (tmp_path / "src" / "Order.cs").write_text(
        "namespace HW.Domain;\n\n"
        "public sealed class Order\n"
        "{\n"
        "    public Guid Id { get; init; }\n"
        "    public decimal Total => Lines.Sum(l => l.Amount);\n"
        "}\n",
        encoding="utf-8",
    )
    (tmp_path / "src" / "OrderService.cs").write_text(
        "public sealed class OrderService\n"
        "{\n"
        '    private const string ConnectionString = "Server=.;password=hunter2000";\n'
        "    public void Cancel(Guid id) { }\n"
        "}\n",
        encoding="utf-8",
    )
    (tmp_path / "secrets").mkdir()
    (tmp_path / "secrets" / "keys.txt").write_text("do-not-read", encoding="utf-8")
    return tmp_path


@pytest.fixture
def settings(tmp_path: Path, workspace_root: Path) -> PlatformSettings:
    """Settings pointed entirely at temporary directories."""
    settings = PlatformSettings()
    settings.environment = "test"
    settings.llm.dry_run = True
    settings.workspace.root = str(workspace_root)
    settings.workspace.build_command = "python -c pass"
    settings.workspace.test_command = "python -c pass"
    settings.memory.long_term_path = str(tmp_path / "long_term.json")
    settings.memory.vector_backend = "memory"
    settings.memory.vector_path = str(tmp_path / "chroma")
    settings.observability.log_dir = str(tmp_path / "logs")
    settings.graph.checkpoint_backend = "none"
    settings.graph.max_iterations = 2
    settings.enabled_plugins = []
    return settings


@pytest.fixture
def workspace(settings: PlatformSettings) -> Workspace:
    return Workspace(settings.workspace)


@pytest.fixture
def hooks() -> HookManager:
    return HookManager()


@pytest.fixture
def llm() -> EchoLLM:
    return EchoLLM()


@pytest.fixture
def memory(tmp_path: Path) -> MemoryManager:
    return MemoryManager(
        short_term=ShortTermMemory(max_items=10),
        long_term=LongTermMemory(tmp_path / "long_term.json"),
        vector=VectorMemory(path=str(tmp_path / "vec"), backend="memory"),
    )


@pytest.fixture
def container(settings: PlatformSettings, llm: EchoLLM) -> Any:
    """A fully wired platform running offline."""
    built: Container = build_container(settings, llm=llm, register_mcp_tools=False)
    try:
        yield built
    finally:
        built.shutdown()

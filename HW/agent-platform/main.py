"""Programmatic entry point.

``cli.py`` is the operator surface; this module is the one to import when the
platform is embedded in a service, a scheduler or a notebook::

    from main import run_command, platform

    with platform() as container:
        result = container.commands.dispatch("/fixbug BUG-123")

Run it directly for a self-check: it builds the whole object graph in dry-run
mode and executes a bug fix end to end without calling the model, which is the
fastest way to confirm an environment is wired correctly.
"""

from __future__ import annotations

import logging
import sys
from contextlib import contextmanager
from typing import Any, Iterator

from configs.settings import PlatformSettings, load_settings
from container import Container, build_container
from workflows.base import WorkflowResult

logger = logging.getLogger(__name__)


@contextmanager
def platform(
    settings: PlatformSettings | None = None,
    *,
    dry_run: bool | None = None,
    **overrides: Any,
) -> Iterator[Container]:
    """Build the platform, yield it, and shut it down afterwards."""
    settings = settings or load_settings()
    if dry_run is not None:
        settings.llm.dry_run = dry_run
    container = build_container(settings, **overrides)
    try:
        yield container
    finally:
        container.shutdown()


def run_command(line: str, **kwargs: Any) -> WorkflowResult:
    """Run one command line against a freshly built platform."""
    with platform(**kwargs) as container:
        return container.commands.dispatch(line)


def run_workflow(name: str, **arguments: Any) -> WorkflowResult:
    """Run one workflow directly."""
    with platform() as container:
        return container.workflows.get(name).run(**arguments)


def _self_check() -> int:
    """Build everything and run a workflow offline. Returns a process exit code."""
    with platform(dry_run=True) as container:
        summary = container.describe()
        print("Agent platform -- self check\n")
        print(f"  environment : {summary['environment']}")
        print(f"  model       : {summary['model']} (dry run)")
        print(f"  workspace   : {summary['workspace']}")
        print(f"  tools       : {len(summary['tools'])}  {', '.join(summary['tools'][:6])} ...")
        print(f"  agents      : {', '.join(summary['agents'])}")
        print(f"  workflows   : {', '.join(summary['workflows'])}")
        print(f"  commands    : {', '.join('/' + name for name in summary['commands'])}")
        print(f"  plugins     : {', '.join(summary['plugins']) or '(none)'}")
        print(f"  skills      : {', '.join(summary['skills'])}")
        print(f"  memory      : {summary['memory']}")
        print(
            "  MCP servers : "
            + ", ".join(
                f"{name}{'' if enabled else ' (disabled)'}"
                for name, enabled in summary["mcps"].items()
            )
        )

        print("\nBug fix graph:\n")
        print(container.workflows.get("bugfix").to_mermaid())

        print("\nRunning /fixbug DEMO-1 offline ...\n")
        result = container.commands.dispatch("/fixbug DEMO-1")
        print(result.summary())
        print(f"\nusage: {container.usage.snapshot()}")

        # In dry-run the model is stubbed, so the run is expected to end without
        # a real success. What matters is that every node executed.
        return 0 if result.state.get("history") else 1


if __name__ == "__main__":  # pragma: no cover
    sys.exit(_self_check())

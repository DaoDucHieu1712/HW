"""Startup health check.

Run this before pointing the platform at a real repository. It verifies what a
running system depends on -- credentials, MCP reachability, the workspace, the
model -- and prints a checklist rather than failing on the first problem, so one
pass tells you everything that needs fixing.

    python scripts/healthcheck.py            # local checks only
    python scripts/healthcheck.py --live     # also make one small model call
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from configs.settings import load_settings  # noqa: E402
from container import build_container  # noqa: E402

OK = "  ok   "
WARN = " warn  "
FAIL = " FAIL  "


def _line(status: str, label: str, detail: str = "") -> None:
    print(f"[{status}] {label}" + (f" -- {detail}" if detail else ""))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Check the platform can run.")
    parser.add_argument("--live", action="store_true", help="make one real model call")
    parser.add_argument("--config", help="path to a platform.yaml")
    args = parser.parse_args(argv)

    settings = load_settings(args.config)
    problems = 0

    print("Agent platform health check\n")

    # -- dependencies ----------------------------------------------------
    for module, consequence in (
        ("anthropic", "the platform cannot call Claude"),
        ("langgraph", "the built-in graph runtime is used instead"),
        ("chromadb", "vector memory falls back to a lexical index"),
        ("mcp", "stdio MCP servers cannot be started"),
        ("opentelemetry", "tracing is disabled"),
    ):
        try:
            __import__(module)
            _line(OK, f"dependency {module}")
        except ImportError:
            required = module == "anthropic"
            problems += required
            _line(FAIL if required else WARN, f"dependency {module}", consequence)

    # -- credentials -----------------------------------------------------
    if os.getenv(settings.llm.api_key_env) or os.getenv("ANTHROPIC_AUTH_TOKEN"):
        _line(OK, "Claude credentials", "from the environment")
    else:
        _line(
            WARN,
            "Claude credentials",
            "no key in the environment; the SDK will try an `ant auth login` profile",
        )

    # -- workspace -------------------------------------------------------
    root = Path(settings.workspace.root).expanduser().resolve()
    if root.is_dir():
        _line(OK, "workspace", str(root))
    else:
        problems += 1
        _line(FAIL, "workspace", f"{root} does not exist")

    if not settings.workspace.allow_writes:
        _line(WARN, "write access", "proposal-only mode: nothing will be written")

    # -- the object graph ------------------------------------------------
    try:
        settings.llm.dry_run = not args.live
        with build_container(settings) as container:
            _line(OK, "container", f"{len(container.tools)} tools, {len(container.agents)} agents")

            for workflow in container.workflows:
                try:
                    workflow.build_spec().validate()
                    _line(OK, f"workflow {workflow.name}")
                except ValueError as exc:
                    problems += 1
                    _line(FAIL, f"workflow {workflow.name}", str(exc))

            for status in container.mcps.health_check():
                if status.healthy:
                    _line(OK, f"mcp {status.server}", f"{status.tools} tools")
                elif "disabled" in status.detail:
                    _line(WARN, f"mcp {status.server}", status.detail)
                else:
                    problems += 1
                    _line(FAIL, f"mcp {status.server}", status.detail)

            for name, client in (
                ("jira", container.tools.get("query_jira").client),
                ("github", container.tools.get("create_pull_request").github),
            ):
                if getattr(client, "configured", False):
                    _line(OK, f"integration {name}")
                else:
                    _line(WARN, f"integration {name}", "not configured")

            if args.live:
                from llms.base import LLMMessage

                response = container.llm.complete(
                    [LLMMessage.user("Reply with the single word: ready")],
                    max_tokens=64,
                )
                _line(
                    OK if response.text else FAIL,
                    "live model call",
                    f"{container.llm.model} -> {response.text[:40]!r}",
                )
    except Exception as exc:  # noqa: BLE001 - the whole point is to report it
        problems += 1
        _line(FAIL, "container", f"{type(exc).__name__}: {exc}")

    print(f"\n{'ready' if problems == 0 else f'{problems} blocking problem(s)'}")
    return 0 if problems == 0 else 1


if __name__ == "__main__":
    sys.exit(main())

"""Command-line interface for the agent platform.

    python cli.py run "/fixbug BUG-123"
    python cli.py fixbug BUG-123 --dry-run
    python cli.py list agents
    python cli.py graph bugfix
    python cli.py mcp health
    python cli.py status
"""

from __future__ import annotations

import argparse
import json
import sys
from typing import Any, Sequence

from commands.base import CommandError
from configs.settings import load_settings
from container import Container, build_container
from workflows.base import WorkflowResult

EXIT_OK = 0
EXIT_FAILED = 1
EXIT_USAGE = 2
EXIT_NEEDS_HUMAN = 3


# -- output ---------------------------------------------------------------


def _print_json(payload: Any) -> None:
    print(json.dumps(payload, indent=2, default=str))


def _print_table(rows: Sequence[dict[str, Any]], columns: Sequence[str]) -> None:
    if not rows:
        print("(none)")
        return
    widths = {
        column: max(len(column), *(len(str(row.get(column, ""))) for row in rows))
        for column in columns
    }
    print("  ".join(column.upper().ljust(widths[column]) for column in columns))
    print("  ".join("-" * widths[column] for column in columns))
    for row in rows:
        print("  ".join(str(row.get(column, "")).ljust(widths[column]) for column in columns))


def _report(result: WorkflowResult, *, as_json: bool) -> int:
    if as_json:
        _print_json(result.to_dict())
    else:
        print()
        print(result.summary())
        artifacts = result.state.get("artifacts") or {}
        if artifacts.get("plan_summary"):
            print(f"\nPlan: {artifacts['plan_summary']}")
        if artifacts.get("root_cause"):
            print(f"Root cause: {artifacts['root_cause']}")
        changes = result.state.get("code_changes") or []
        if changes:
            print("\nChanges:")
            for change in changes:
                print(f"  {change.get('action', 'modified'):9} {change.get('path', '')}")
        findings = result.state.get("review_comments") or []
        if findings:
            print("\nFindings:")
            for finding in findings:
                location = f"{finding.get('path', '')}:{finding.get('line', 0)}"
                print(f"  [{finding.get('severity', 'minor'):7}] {location} {finding.get('message', '')}")
        if artifacts.get("pr_title"):
            print(f"\nPull request: {artifacts['pr_title']}")
        errors = result.state.get("errors") or []
        if errors:
            print("\nErrors:")
            for error in errors:
                print(f"  - {error}")
        if result.error:
            print(f"\nRun failed: {result.error}")

    if result.needs_human:
        return EXIT_NEEDS_HUMAN
    return EXIT_OK if result.succeeded else EXIT_FAILED


# -- sub-commands ---------------------------------------------------------


def cmd_run(container: Container, args: argparse.Namespace) -> int:
    try:
        result = container.commands.dispatch(args.command_line)
    except CommandError as exc:
        print(f"error: {exc}", file=sys.stderr)
        print(container.commands.help_text(), file=sys.stderr)
        return EXIT_USAGE
    return _report(result, as_json=args.json)


def cmd_workflow(container: Container, args: argparse.Namespace) -> int:
    """Run a workflow directly, bypassing command parsing."""
    arguments = dict(pair.split("=", 1) for pair in args.set or [])
    try:
        result = container.workflows.get(args.workflow).run(**arguments)
    except (KeyError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return EXIT_USAGE
    return _report(result, as_json=args.json)


def cmd_list(container: Container, args: argparse.Namespace) -> int:
    what = args.what
    if what == "agents":
        rows = container.agents.describe()
        for row in rows:
            row["tools"] = ", ".join(row["tools"])[:60]
            row["skills"] = ", ".join(row["skills"])
        _print_table(rows, ["name", "tools", "skills"]) if not args.json else _print_json(rows)
    elif what == "tools":
        rows = [
            {
                "name": tool.name,
                "read_only": tool.read_only,
                "tags": ", ".join(tool.tags),
                "description": tool.description[:70],
            }
            for tool in sorted(container.tools, key=lambda t: t.name)
        ]
        _print_table(rows, ["name", "read_only", "tags", "description"]) if not args.json else _print_json(rows)
    elif what == "workflows":
        rows = container.workflows.describe()
        for row in rows:
            row["required_arguments"] = ", ".join(row["required_arguments"])
            row["description"] = row["description"][:70]
        _print_table(rows, ["name", "required_arguments", "description"]) if not args.json else _print_json(rows)
    elif what == "commands":
        rows = container.commands.describe()
        _print_table(rows, ["name", "workflow", "help"]) if not args.json else _print_json(rows)
    elif what == "plugins":
        rows = container.plugins.describe()
        _print_table(rows, ["name", "registered", "stages"]) if not args.json else _print_json(rows)
    elif what == "skills":
        rows = [skill.to_dict() for skill in container.skills]
        for row in rows:
            row["tags"] = ", ".join(row["tags"])
            row["description"] = row["description"][:70]
        _print_table(rows, ["name", "tags", "description"]) if not args.json else _print_json(rows)
    elif what == "mcps":
        rows = container.mcps.describe()
        for row in rows:
            row["tools"] = ", ".join(row["tools"])[:50]
        _print_table(rows, ["name", "enabled", "transport", "tools"]) if not args.json else _print_json(rows)
    else:  # pragma: no cover - argparse restricts the choices
        return EXIT_USAGE
    return EXIT_OK


def cmd_graph(container: Container, args: argparse.Namespace) -> int:
    try:
        workflow = container.workflows.get(args.workflow)
    except KeyError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return EXIT_USAGE
    print(workflow.to_mermaid())
    return EXIT_OK


def cmd_mcp(container: Container, args: argparse.Namespace) -> int:
    statuses = [status.to_dict() for status in container.mcps.health_check(args.server)]
    if args.json:
        _print_json(statuses)
    else:
        _print_table(statuses, ["server", "healthy", "tools", "latency_ms", "detail"])
    return EXIT_OK if all(status["healthy"] for status in statuses) else EXIT_FAILED


def cmd_status(container: Container, args: argparse.Namespace) -> int:
    _print_json(container.describe())
    return EXIT_OK


# -- parser ---------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="agent-platform",
        description="Agentic AI platform: multi-agent workflows over your codebase.",
    )
    parser.add_argument("--config", help="path to a platform.yaml to load instead of the default")
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="run the graph without calling the model (no tokens are spent)",
    )
    parser.add_argument("--json", action="store_true", help="machine-readable output")
    parser.add_argument("--log-level", help="override the configured log level")

    sub = parser.add_subparsers(dest="subcommand", required=True)

    run = sub.add_parser("run", help='run a command line, e.g. "/fixbug BUG-123"')
    run.add_argument("command_line")
    run.set_defaults(handler=cmd_run)

    workflow = sub.add_parser("workflow", help="run a workflow directly")
    workflow.add_argument("workflow")
    workflow.add_argument(
        "--set", action="append", metavar="KEY=VALUE", help="workflow argument"
    )
    workflow.set_defaults(handler=cmd_workflow)

    listing = sub.add_parser("list", help="list what is registered")
    listing.add_argument(
        "what",
        choices=["agents", "tools", "workflows", "commands", "plugins", "skills", "mcps"],
    )
    listing.set_defaults(handler=cmd_list)

    graph = sub.add_parser("graph", help="print a workflow graph as mermaid")
    graph.add_argument("workflow")
    graph.set_defaults(handler=cmd_graph)

    mcp = sub.add_parser("mcp", help="MCP server operations")
    mcp.add_argument("action", choices=["health"])
    mcp.add_argument("server", nargs="?", help="check one server instead of all")
    mcp.set_defaults(handler=cmd_mcp)

    status = sub.add_parser("status", help="print the wired-up platform")
    status.set_defaults(handler=cmd_status)

    # Shorthands for the five documented commands.
    for name, argument, help_text in (
        ("fixbug", "issue_key", "fix a bug from its ticket"),
        ("unittest", "target", "generate unit tests for a file"),
        ("newfeature", "requirement", "build a new feature"),
        ("analyzelog", "correlation_id", "analyse an incident"),
    ):
        shortcut = sub.add_parser(name, help=help_text)
        shortcut.add_argument(argument)
        shortcut.add_argument("--max-iterations", type=int)
        shortcut.set_defaults(handler=_shortcut_handler(name, argument))

    review = sub.add_parser("review", help="review changed files")
    review.add_argument("paths", nargs="*")
    review.set_defaults(
        handler=lambda container, args: cmd_run(
            container,
            argparse.Namespace(
                command_line="/review " + " ".join(args.paths), json=args.json
            ),
        )
    )
    return parser


def _shortcut_handler(command: str, argument: str):
    """Turn ``cli.py fixbug BUG-1`` into ``cli.py run "/fixbug BUG-1"``."""

    def handler(container: Container, args: argparse.Namespace) -> int:
        line = f"/{command} {getattr(args, argument)}"
        if getattr(args, "max_iterations", None):
            line += f" --max-iterations {args.max_iterations}"
        return cmd_run(container, argparse.Namespace(command_line=line, json=args.json))

    return handler


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)

    settings = load_settings(args.config)
    if args.dry_run:
        settings.llm.dry_run = True
    if args.log_level:
        settings.observability.log_level = args.log_level.upper()

    with build_container(settings) as container:
        return int(args.handler(container, args))


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())

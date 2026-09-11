"""The commands the platform ships with.

Each one maps to exactly one workflow. Keeping the mapping one-to-one is what
makes the surface predictable: what ``/fixbug`` does is what the bug fix
workflow does, and there is nowhere else for behaviour to hide.
"""

from __future__ import annotations

from typing import Any

from .base import Command, CommandError, ParsedCommand


class FixBugCommand(Command):
    name = "fixbug"
    aliases = ("bugfix", "fix")
    workflow = "bugfix"
    help = "Fix a bug end to end, from the ticket to a reviewed pull request."
    usage = (
        "/fixbug <ISSUE-KEY> [--correlation-id <id>] [--notes <text>] "
        "[--max-iterations <n>]"
    )

    def to_arguments(self, parsed: ParsedCommand) -> dict[str, Any]:
        if not parsed.positional:
            raise CommandError(f"an issue key is required.  Usage: {self.usage}")
        return {
            "issue_key": parsed.positional[0],
            "correlation_id": parsed.option("correlation-id"),
            "notes": parsed.option("notes"),
            "stack_trace": parsed.option("stack-trace"),
            "max_iterations": parsed.int_option("max-iterations"),
        }


class ReviewCommand(Command):
    name = "review"
    aliases = ("codereview",)
    workflow = "review"
    help = "Review changed files for correctness, security, performance and design."
    usage = "/review [path ...] [--target <description>] [--objective <text>]"

    def to_arguments(self, parsed: ParsedCommand) -> dict[str, Any]:
        return {
            "paths": parsed.positional,
            "target": parsed.option("target"),
            "objective": parsed.option("objective"),
            "diff": parsed.option("diff"),
        }


class UnitTestCommand(Command):
    name = "unittest"
    aliases = ("gentest", "test")
    workflow = "unittest"
    help = "Generate and run unit tests for a file or a symbol."
    usage = "/unittest <path> [--symbol <name>] [--framework xunit] [--focus <text>]"

    def to_arguments(self, parsed: ParsedCommand) -> dict[str, Any]:
        if not parsed.positional:
            raise CommandError(f"a target path is required.  Usage: {self.usage}")
        return {
            "target": parsed.positional[0],
            "symbol": parsed.option("symbol"),
            "framework": parsed.option("framework"),
            "focus": parsed.option("focus"),
            "max_iterations": parsed.int_option("max-iterations"),
        }


class NewFeatureCommand(Command):
    name = "newfeature"
    aliases = ("feature",)
    workflow = "feature"
    help = "Build a new feature as a tested vertical slice."
    usage = (
        '/newfeature "<requirement>" [--issue-key <KEY>] [--constraints <text>] '
        "[--out-of-scope <text>]"
    )

    def to_arguments(self, parsed: ParsedCommand) -> dict[str, Any]:
        requirement = " ".join(parsed.positional).strip()
        if not requirement:
            raise CommandError(f"a requirement is required.  Usage: {self.usage}")
        return {
            "requirement": requirement,
            "issue_key": parsed.option("issue-key"),
            "constraints": parsed.option("constraints"),
            "out_of_scope": parsed.option("out-of-scope"),
            "objective": parsed.option("objective"),
            "max_iterations": parsed.int_option("max-iterations"),
        }


class AnalyzeLogCommand(Command):
    name = "analyzelog"
    aliases = ("analyselog", "logs", "trace")
    workflow = "analyzelog"
    help = "Reconstruct an incident timeline from logs and traces."
    usage = (
        "/analyzelog [--correlation-id <id>] [--query <text>] [--since <minutes>] "
        "[--level Error]"
    )

    def to_arguments(self, parsed: ParsedCommand) -> dict[str, Any]:
        correlation_id = parsed.option("correlation-id") or (
            parsed.positional[0] if parsed.positional else None
        )
        query = parsed.option("query")
        if not correlation_id and not query:
            raise CommandError(
                f"give a correlation id or a --query.  Usage: {self.usage}"
            )
        return {
            "correlation_id": correlation_id,
            "query": query,
            "since_minutes": parsed.int_option("since"),
            "level": parsed.option("level"),
            "component": parsed.option("component"),
        }


#: Instantiated once; commands are stateless.
BUILTIN_COMMANDS: tuple[Command, ...] = (
    FixBugCommand(),
    ReviewCommand(),
    UnitTestCommand(),
    NewFeatureCommand(),
    AnalyzeLogCommand(),
)

__all__ = [
    "AnalyzeLogCommand",
    "BUILTIN_COMMANDS",
    "FixBugCommand",
    "NewFeatureCommand",
    "ReviewCommand",
    "UnitTestCommand",
]

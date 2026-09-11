"""Command registry and dispatch."""

from __future__ import annotations

import logging
from typing import Any, Iterable, Iterator

from workflows.base import WorkflowResult
from workflows.registry import WorkflowRegistry

from .base import Command, CommandError, ParsedCommand, parse_command_line
from .builtin import BUILTIN_COMMANDS

logger = logging.getLogger(__name__)


class CommandRegistry:
    """Resolves a typed command line to a command, and runs it."""

    def __init__(self, workflows: WorkflowRegistry) -> None:
        self.workflows = workflows
        self._commands: dict[str, Command] = {}

    # -- registration ----------------------------------------------------

    def register(self, command: Command, *, replace: bool = False) -> Command:
        if not self.workflows.has(command.workflow):
            raise ValueError(
                f"command /{command.name} maps to unknown workflow {command.workflow!r}"
            )
        for name in command.names():
            if name in self._commands and not replace:
                raise ValueError(f"command name {name!r} is already registered")
            self._commands[name] = command
        return command

    def register_all(self, commands: Iterable[Command], *, replace: bool = False) -> None:
        for command in commands:
            self.register(command, replace=replace)

    # -- lookup ----------------------------------------------------------

    def get(self, name: str) -> Command:
        key = name.lstrip("/").lower()
        try:
            return self._commands[key]
        except KeyError as exc:
            raise CommandError(
                f"unknown command /{key}.  Try: {', '.join('/' + n for n in self.names())}"
            ) from exc

    def has(self, name: str) -> bool:
        return name.lstrip("/").lower() in self._commands

    def names(self) -> list[str]:
        """Primary names, without aliases."""
        return sorted({command.name for command in self._commands.values()})

    def describe(self) -> list[dict[str, Any]]:
        seen: dict[str, Command] = {c.name: c for c in self._commands.values()}
        return [seen[name].to_dict() for name in sorted(seen)]

    def help_text(self) -> str:
        lines = ["Commands:"]
        for entry in self.describe():
            lines.append(f"  {entry['usage'] or entry['name']}")
            lines.append(f"      {entry['help']}")
        return "\n".join(lines)

    # -- dispatch --------------------------------------------------------

    def dispatch(self, line: str) -> WorkflowResult:
        """Parse and run a command line."""
        parsed: ParsedCommand = parse_command_line(line)
        command = self.get(parsed.name)
        logger.info("dispatching /%s -> workflow %s", command.name, command.workflow)
        return command.execute(parsed, self.workflows)

    def __iter__(self) -> Iterator[Command]:
        return iter(dict.fromkeys(self._commands.values()))

    def __len__(self) -> int:
        return len({command.name for command in self._commands.values()})


def build_commands(workflows: WorkflowRegistry) -> CommandRegistry:
    registry = CommandRegistry(workflows)
    registry.register_all(BUILTIN_COMMANDS)
    logger.info("registered %d commands: %s", len(registry), ", ".join(registry.names()))
    return registry


__all__ = ["CommandRegistry", "build_commands"]

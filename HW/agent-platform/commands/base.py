"""Command contract.

A command is the user-facing surface: it parses a line like ``/fixbug BUG-123``
into the keyword arguments a workflow's ``seed_state`` expects. Parsing lives
here rather than in the workflow so a workflow can be driven from a CLI, an HTTP
endpoint or a chat bot without changing it.
"""

from __future__ import annotations

import abc
import shlex
from dataclasses import dataclass, field
from typing import Any

from workflows.base import WorkflowResult
from workflows.registry import WorkflowRegistry


class CommandError(ValueError):
    """The command line could not be parsed or is missing an argument."""


@dataclass(slots=True)
class ParsedCommand:
    """A command line split into its name, positional and keyword parts."""

    name: str
    positional: list[str] = field(default_factory=list)
    options: dict[str, str] = field(default_factory=dict)

    def option(self, key: str, default: str | None = None) -> str | None:
        return self.options.get(key, default)

    def int_option(self, key: str, default: int | None = None) -> int | None:
        raw = self.options.get(key)
        if raw is None:
            return default
        try:
            return int(raw)
        except ValueError as exc:
            raise CommandError(f"--{key} must be a whole number, got {raw!r}") from exc

    def flag(self, key: str) -> bool:
        return self.options.get(key, "").lower() in {"", "1", "true", "yes", "on"} and key in self.options


def parse_command_line(line: str) -> ParsedCommand:
    """Split ``/name arg --key value --flag`` into its parts.

    Accepts the leading slash or not, so the same parser serves a chat surface
    and a shell.
    """
    text = line.strip()
    if not text:
        raise CommandError("empty command")
    if text.startswith("/"):
        text = text[1:]

    try:
        tokens = shlex.split(text)
    except ValueError as exc:  # unbalanced quotes
        raise CommandError(f"could not parse the command: {exc}") from exc
    if not tokens:
        raise CommandError("empty command")

    name, *rest = tokens
    positional: list[str] = []
    options: dict[str, str] = {}

    index = 0
    while index < len(rest):
        token = rest[index]
        if token.startswith("--"):
            key, separator, inline = token[2:].partition("=")
            if separator:
                options[key] = inline
            elif index + 1 < len(rest) and not rest[index + 1].startswith("--"):
                options[key] = rest[index + 1]
                index += 1
            else:
                options[key] = ""  # a bare flag
        else:
            positional.append(token)
        index += 1

    return ParsedCommand(name=name.lower(), positional=positional, options=options)


class Command(abc.ABC):
    """Maps one command onto one workflow."""

    #: What the user types, without the slash.
    name: str = ""
    #: Alternative spellings.
    aliases: tuple[str, ...] = ()
    #: The workflow this command runs.
    workflow: str = ""
    #: One line of help.
    help: str = ""
    #: Usage string shown on a parse error.
    usage: str = ""

    @abc.abstractmethod
    def to_arguments(self, parsed: ParsedCommand) -> dict[str, Any]:
        """Translate the parsed line into workflow arguments."""

    def execute(self, parsed: ParsedCommand, workflows: WorkflowRegistry) -> WorkflowResult:
        arguments = self.to_arguments(parsed)
        return workflows.get(self.workflow).run(**arguments)

    def names(self) -> tuple[str, ...]:
        return (self.name, *self.aliases)

    def to_dict(self) -> dict[str, Any]:
        return {
            "name": f"/{self.name}",
            "aliases": [f"/{alias}" for alias in self.aliases],
            "workflow": self.workflow,
            "help": self.help,
            "usage": self.usage,
        }


__all__ = ["Command", "CommandError", "ParsedCommand", "parse_command_line"]

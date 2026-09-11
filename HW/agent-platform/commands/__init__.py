"""Command surface: parsing, registry and the built-in commands."""

from .base import Command, CommandError, ParsedCommand, parse_command_line
from .builtin import BUILTIN_COMMANDS
from .registry import CommandRegistry, build_commands

__all__ = [
    "BUILTIN_COMMANDS",
    "Command",
    "CommandError",
    "CommandRegistry",
    "ParsedCommand",
    "build_commands",
    "parse_command_line",
]

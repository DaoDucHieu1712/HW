"""Build, test and test-generation tools.

``build_solution`` and ``run_tests`` shell out to the configured commands and
return a structured outcome; parsing the output into pass/fail counts is what
lets the graph route on evidence instead of on the model's opinion.
"""

from __future__ import annotations

import re
from typing import Any

from llms.base import LLMClient, LLMMessage

from .base import Tool, ToolError, ToolResult
from .workspace import CommandOutcome, Workspace, WorkspaceError

#: `dotnet test` summary line, e.g. "Passed! - Failed: 0, Passed: 12, Skipped: 1"
_DOTNET_SUMMARY = re.compile(
    r"Failed:\s*(?P<failed>\d+).*?Passed:\s*(?P<passed>\d+).*?Skipped:\s*(?P<skipped>\d+)",
    re.IGNORECASE | re.DOTALL,
)
#: MSBuild error line, e.g. "Order.cs(42,17): error CS0103: ..."
_BUILD_ERROR = re.compile(r"^(?P<file>.+?)\((?P<line>\d+),\d+\):\s*error\s+(?P<code>\w+):\s*(?P<message>.+)$")


class BuildSolutionTool(Tool):
    name = "build_solution"
    description = (
        "Compile the solution and return the result. On failure the compiler "
        "errors are returned in structured form (file, line, code, message) so "
        "they can be fixed directly. Run this after every code change."
    )
    read_only = False
    tags = ("build", "verify")
    input_schema = {
        "type": "object",
        "properties": {
            "project": {
                "type": "string",
                "description": "Optional project or solution path to build instead of the default.",
            },
            "configuration": {
                "type": "string",
                "description": "Build configuration, e.g. Debug or Release.",
            },
        },
        "required": [],
    }

    def __init__(self, workspace: Workspace, *, command: str | None = None) -> None:
        self.workspace = workspace
        self.command = command or workspace.settings.build_command

    def _execute(
        self, project: str | None = None, configuration: str | None = None
    ) -> ToolResult:
        command = self.command
        if project:
            # Validate the path before it reaches the command line.
            self.workspace.resolve(project, must_exist=True)
            command = f"{command} {project}"
        if configuration:
            command = f"{command} -c {configuration}"

        try:
            outcome = self.workspace.run(command)
        except WorkspaceError as exc:
            raise ToolError(str(exc)) from exc

        errors = parse_build_errors(outcome)
        payload: dict[str, Any] = {
            **outcome.to_dict(),
            "errors": errors[:25],
            "error_count": len(errors),
        }
        return ToolResult(
            tool=self.name,
            ok=outcome.ok,
            data=payload,
            error=None if outcome.ok else f"build failed with {len(errors)} error(s)",
            metadata={"exit_code": outcome.exit_code},
        )


class RunTestsTool(Tool):
    name = "run_tests"
    description = (
        "Run the test suite, optionally filtered to a subset, and return the "
        "pass/fail counts plus the failing test output. This is the evidence the "
        "workflow routes on: do not claim a fix works until this reports success."
    )
    read_only = False
    tags = ("test", "verify")
    input_schema = {
        "type": "object",
        "properties": {
            "filter": {
                "type": "string",
                "description": "Test filter expression, e.g. FullyQualifiedName~OrderTests",
            },
            "project": {
                "type": "string",
                "description": "Optional test project path.",
            },
        },
        "required": [],
    }

    def __init__(self, workspace: Workspace, *, command: str | None = None) -> None:
        self.workspace = workspace
        self.command = command or workspace.settings.test_command

    def _execute(self, filter: str | None = None, project: str | None = None) -> ToolResult:  # noqa: A002
        command = self.command
        if project:
            self.workspace.resolve(project, must_exist=True)
            command = f"{command} {project}"
        if filter:
            command = f'{command} --filter "{filter}"'

        try:
            outcome = self.workspace.run(command)
        except WorkspaceError as exc:
            raise ToolError(str(exc)) from exc

        summary = parse_test_summary(outcome)
        payload = {**outcome.to_dict(), **summary}
        return ToolResult(
            tool=self.name,
            ok=outcome.ok,
            data=payload,
            error=None if outcome.ok else f"{summary.get('failed', '?')} test(s) failed",
            metadata={"exit_code": outcome.exit_code},
        )


class GenerateUnitTestTool(Tool):
    name = "generate_unit_test"
    description = (
        "Generate a unit test for a specific symbol in a source file. Returns the "
        "test source as text -- it is not written to disk, so review it and then "
        "use write_file. Covers the happy path, boundaries, and failure modes."
    )
    read_only = True
    tags = ("test", "generate")
    input_schema = {
        "type": "object",
        "properties": {
            "path": {"type": "string", "description": "Workspace-relative source file."},
            "symbol": {
                "type": "string",
                "description": "Class or method to test, e.g. OrderService.Cancel",
            },
            "framework": {
                "type": "string",
                "description": "Test framework, e.g. xunit, nunit, pytest. Defaults to xunit.",
            },
        },
        "required": ["path", "symbol"],
    }

    def __init__(self, workspace: Workspace, llm: LLMClient, *, max_tokens: int = 4000) -> None:
        self.workspace = workspace
        self.llm = llm
        self.max_tokens = max_tokens

    def _execute(self, path: str, symbol: str, framework: str | None = None) -> ToolResult:
        try:
            source = self.workspace.read_text(path)
        except WorkspaceError as exc:
            raise ToolError(str(exc)) from exc

        target_framework = framework or "xunit"
        system = (
            "You write focused unit tests. Test observable behaviour, not implementation "
            "details. Cover the happy path, boundary values, and the failure modes the code "
            "actually has. Use the Arrange/Act/Assert shape and name each test after the "
            "behaviour it pins down. Return only the test source file, no commentary."
        )
        prompt = (
            f"Framework: {target_framework}\n"
            f"Symbol under test: {symbol}\n"
            f"Source file: {path}\n\n"
            f"```\n{source}\n```"
        )
        response = self.llm.complete(
            [LLMMessage.user(prompt)], system=system, max_tokens=self.max_tokens
        )
        code = _strip_fence(response.text)
        if not code.strip():
            raise ToolError("the model returned no test source")
        return ToolResult.success(
            self.name, code, symbol=symbol, framework=target_framework, source_path=path
        )


# -- parsing helpers ------------------------------------------------------


def parse_build_errors(outcome: CommandOutcome) -> list[dict[str, Any]]:
    """Extract MSBuild-style compiler errors from command output."""
    errors: list[dict[str, Any]] = []
    seen: set[tuple[str, str, str]] = set()
    for line in (outcome.stdout + "\n" + outcome.stderr).splitlines():
        match = _BUILD_ERROR.match(line.strip())
        if not match:
            continue
        key = (match.group("file"), match.group("line"), match.group("code"))
        if key in seen:
            continue
        seen.add(key)
        errors.append(
            {
                "file": match.group("file").strip(),
                "line": int(match.group("line")),
                "code": match.group("code"),
                "message": match.group("message").strip(),
            }
        )
    return errors


def parse_test_summary(outcome: CommandOutcome) -> dict[str, Any]:
    """Pull pass/fail/skip counts out of the runner output.

    Falls back to the exit code when the summary line is absent, so an unknown
    runner still produces a usable verdict.
    """
    match = _DOTNET_SUMMARY.search(outcome.stdout + outcome.stderr)
    if match:
        failed = int(match.group("failed"))
        return {
            "passed": int(match.group("passed")),
            "failed": failed,
            "skipped": int(match.group("skipped")),
            "success": failed == 0 and outcome.exit_code == 0,
        }
    return {
        "passed": None,
        "failed": None,
        "skipped": None,
        "success": outcome.ok,
        "note": "no recognised test summary line; using the exit code",
    }


def _strip_fence(text: str) -> str:
    stripped = text.strip()
    if not stripped.startswith("```"):
        return stripped
    body = stripped.split("\n", 1)[-1]
    return body.rsplit("```", 1)[0].strip()


__all__ = [
    "BuildSolutionTool",
    "GenerateUnitTestTool",
    "RunTestsTool",
    "parse_build_errors",
    "parse_test_summary",
]

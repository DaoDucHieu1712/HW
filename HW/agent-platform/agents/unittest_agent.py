"""Unit test agent -- writes tests that would have caught the defect, then runs them."""

from __future__ import annotations

from typing import Any

from graph.state import AgentState, Status

from .base import AgentConfig, AgentResult, BaseAgent


class UnitTestAgent(BaseAgent):
    """Generates and executes tests.

    For a bug fix the important test is the one that fails on the old code; the
    agent is told to reason about that explicitly, because a test written after
    the fix and never seen red proves nothing.
    """

    name = "unittest"
    description = (
        "Writes unit tests for the changed code -- happy path, boundaries and the "
        "failure modes that actually exist -- then runs the suite and reports the result."
    )
    default_config = AgentConfig(
        tools=["read_file", "write_file", "generate_unit_test", "run_tests"],
        skills=["xunit", "integration_testing"],
        max_tokens=16000,
        max_tool_iterations=12,
        include_skill_examples=True,
    )
    output_schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string", "description": "What is now covered."},
            "test_files": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Test files created or extended.",
            },
            "cases": {
                "type": "array",
                "description": "The cases written, and what each one pins down.",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string"},
                        "group": {
                            "type": "string",
                            "enum": ["happy_path", "boundary", "failure_mode", "regression"],
                        },
                        "behaviour": {"type": "string"},
                    },
                    "required": ["name", "group"],
                },
            },
            "tests_passed": {"type": "boolean", "description": "Did run_tests succeed?"},
            "passed": {"type": "integer"},
            "failed": {"type": "integer"},
            "skipped": {"type": "integer"},
            "output": {"type": "string", "description": "Failure output, when tests failed."},
            "would_have_caught_the_bug": {
                "type": "boolean",
                "description": "True only if a test here fails against the pre-fix code.",
            },
        },
        "required": ["summary", "tests_passed"],
        "additionalProperties": True,
    }

    def build_prompt(self, state: AgentState) -> str:
        changes = state.get("code_changes") or []
        sections = [
            f"# Objective\n{state.get('objective') or state.get('task')}",
            "# Code that changed\n"
            + (
                "\n".join(
                    f"- {change.get('action', 'modified')} {change.get('path', '')}: "
                    f"{change.get('rationale', '')}"
                    for change in changes
                )
                or "(no recorded changes -- inspect the workspace)"
            ),
        ]

        artifacts = state.get("artifacts") or {}
        if artifacts.get("root_cause"):
            sections.append(
                f"# Root cause of the defect\n{artifacts['root_cause']}\n\n"
                "Write the test that fails against the code as it was *before* the fix. "
                "That is the test that has value; the rest is coverage."
            )
        if artifacts.get("verification"):
            sections.append(f"# The plan's verification criterion\n{artifacts['verification']}")

        failures = [
            result
            for result in (state.get("test_results") or [])
            if result.get("kind") == "unit" and not result.get("success")
        ]
        if failures:
            sections.append(
                "# The previous test run failed -- fix the cause, do not delete the test\n"
                f"```\n{str(failures[-1].get('output', ''))[-3000:]}\n```"
            )

        sections.append(
            "Use the fixtures, builders and harnesses the repository already has "
            "before writing new ones. Assert on observable behaviour, not on how it "
            "was reached. Run the suite before answering and report the real counts."
        )
        return "\n\n".join(sections)

    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        if not result.ok:
            return {
                "status": Status.NEEDS_REVISION.value,
                "errors": [f"unittest: {result.error}"],
                "history": [result.to_history()],
            }

        payload = result.payload
        passed = bool(payload.get("tests_passed"))
        patch: dict[str, Any] = {
            "test_results": [
                {
                    "kind": "unit",
                    "success": passed,
                    "passed": int(payload.get("passed") or 0),
                    "failed": int(payload.get("failed") or 0),
                    "skipped": int(payload.get("skipped") or 0),
                    "output": str(payload.get("output", ""))[:4000],
                    "command": "run_tests",
                }
            ],
            "status": (Status.REVIEWING if passed else Status.NEEDS_REVISION).value,
            "history": [result.to_history()],
            "artifacts": {
                "test_files": payload.get("test_files") or [],
                "test_cases": payload.get("cases") or [],
            },
        }
        if not passed:
            patch["errors"] = [f"unittest: {payload.get('failed', '?')} test(s) failing"]
        # A test written but never seen red is worth flagging, not failing on.
        if payload.get("would_have_caught_the_bug") is False and (state.get("artifacts") or {}).get(
            "root_cause"
        ):
            patch["artifacts"]["test_warning"] = (
                "No test here is known to fail against the pre-fix code; the regression "
                "may not actually be pinned down."
            )
        return patch


__all__ = ["UnitTestAgent"]

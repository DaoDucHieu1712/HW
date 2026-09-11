"""Coding agent -- implements the plan and proves the solution still compiles."""

from __future__ import annotations

from typing import Any

from graph.state import AgentState, Status

from .base import AgentConfig, AgentResult, BaseAgent


class CodingAgent(BaseAgent):
    """Writes the change, then builds.

    The agent is expected to call ``build_solution`` itself before answering.
    The build result recorded here is evidence; the graph routes on it rather
    than on the agent's own claim that the change works.
    """

    name = "coding"
    description = (
        "Implements the planned change in the workspace, following the existing "
        "conventions of the code it edits, and builds to prove it compiles."
    )
    default_config = AgentConfig(
        tools=["search_code", "read_file", "write_file", "build_solution"],
        skills=["cqrs", "clean_architecture", "efcore"],
        max_tokens=16000,
        max_tool_iterations=14,
        include_skill_examples=True,
    )
    output_schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string", "description": "What was changed and why."},
            "changes": {
                "type": "array",
                "description": "Every file touched.",
                "items": {
                    "type": "object",
                    "properties": {
                        "path": {"type": "string"},
                        "action": {
                            "type": "string",
                            "enum": ["created", "modified", "deleted"],
                        },
                        "rationale": {"type": "string"},
                    },
                    "required": ["path", "action"],
                },
            },
            "build_succeeded": {
                "type": "boolean",
                "description": "Result of the build you ran. Report it honestly.",
            },
            "build_output": {"type": "string", "description": "Errors, when the build failed."},
            "follow_up": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Work deliberately left out of this change.",
            },
        },
        "required": ["summary", "changes", "build_succeeded"],
        "additionalProperties": True,
    }

    def build_prompt(self, state: AgentState) -> str:
        plan = state.get("plan") or []
        sections = [
            f"# Objective\n{state.get('objective') or state.get('task')}",
            "# Plan\n" + ("\n".join(f"{i}. {step}" for i, step in enumerate(plan, 1)) or "(none)"),
        ]

        artifacts = state.get("artifacts") or {}
        if artifacts.get("root_cause"):
            sections.append(f"# Root cause\n{artifacts['root_cause']}")
        if artifacts.get("files_to_change"):
            sections.append(
                "# Files the plan expects to change\n"
                + "\n".join(f"- {path}" for path in artifacts["files_to_change"])
            )

        failures = _failure_context(state)
        if failures:
            sections.append("# What is currently failing -- fix this\n" + failures)

        findings = state.get("review_comments") or []
        if findings:
            sections.append(
                "# Review findings to address\n"
                + "\n".join(
                    f"- [{c.get('severity', 'minor')}] {c.get('path', '')}:{c.get('line', '')} "
                    f"{c.get('message', '')}"
                    for c in findings[:20]
                )
            )

        sections.append(
            "Read a file before you edit it, and match the conventions of the code "
            "around it -- naming, error handling, comment density. Make the smallest "
            "change that does the job; do not refactor code the task did not ask you "
            "to touch. Run build_solution before you answer, and report the real "
            "result: a failed build reported as a success costs the whole run."
        )
        return "\n\n".join(sections)

    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        if not result.ok:
            return {
                "status": Status.NEEDS_REVISION.value,
                "errors": [f"coding: {result.error}"],
                "history": [result.to_history()],
            }

        payload = result.payload
        changes = [
            {
                "path": change.get("path", ""),
                "action": change.get("action", "modified"),
                "rationale": change.get("rationale", ""),
                "applied": True,
            }
            for change in (payload.get("changes") or [])
            if change.get("path")
        ]
        built = bool(payload.get("build_succeeded"))

        patch: dict[str, Any] = {
            "code_changes": changes,
            "test_results": [
                {
                    "kind": "build",
                    "success": built,
                    "output": str(payload.get("build_output", ""))[:4000],
                    "command": "build_solution",
                }
            ],
            "status": (Status.TESTING if built else Status.NEEDS_REVISION).value,
            "history": [result.to_history()],
            "artifacts": {"implementation_summary": payload.get("summary", "")},
        }
        if not built:
            patch["errors"] = ["coding: the build failed"]
        if payload.get("follow_up"):
            patch["artifacts"]["follow_up"] = payload["follow_up"]
        return patch


def _failure_context(state: AgentState) -> str:
    """The build/test output the coding agent has to make green."""
    parts: list[str] = []
    for result in (state.get("test_results") or [])[-2:]:
        if not result.get("success"):
            parts.append(
                f"## {result.get('kind', 'run')} failure\n```\n"
                f"{str(result.get('output', ''))[-3000:]}\n```"
            )
    return "\n\n".join(parts)


__all__ = ["CodingAgent"]

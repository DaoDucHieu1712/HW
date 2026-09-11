"""Documentation agent -- writes the record of what changed and why."""

from __future__ import annotations

from typing import Any

from graph.state import AgentState, Status, latest_test_result

from .base import AgentConfig, AgentResult, BaseAgent


class DocumentationAgent(BaseAgent):
    """Produces the pull request body and any doc updates.

    It writes from the recorded evidence -- the changes, the test results, the
    review findings -- rather than from a fresh reading of the code, so the
    description matches what the run actually did.
    """

    name = "documentation"
    description = (
        "Writes the pull request description and updates the documentation the "
        "change makes stale."
    )
    default_config = AgentConfig(
        tools=["read_file", "search_code", "write_file"],
        skills=["clean_architecture"],
        max_tokens=8000,
        max_tool_iterations=6,
        effort="medium",
    )
    output_schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string"},
            "pr_title": {
                "type": "string",
                "description": "Imperative, under 72 characters, no ticket prefix.",
            },
            "pr_body": {
                "type": "string",
                "description": "Markdown: what changed, why, how it was verified, what to watch.",
            },
            "docs_updated": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Documentation files written.",
            },
            "release_note": {"type": "string", "description": "One line for the changelog."},
        },
        "required": ["summary", "pr_title", "pr_body"],
        "additionalProperties": True,
    }

    def build_prompt(self, state: AgentState) -> str:
        changes = state.get("code_changes") or []
        tests = latest_test_result(state, "unit")
        artifacts = state.get("artifacts") or {}

        sections = [
            f"# Objective\n{state.get('objective') or state.get('task')}",
            "# Changes\n"
            + (
                "\n".join(
                    f"- {change.get('action', 'modified')} {change.get('path', '')}: "
                    f"{change.get('rationale', '')}"
                    for change in changes
                )
                or "(none recorded)"
            ),
            "# Verification\n"
            + (
                "no test results recorded"
                if tests is None
                else f"tests {'passed' if tests.get('success') else 'FAILED'} "
                f"(passed={tests.get('passed')} failed={tests.get('failed')})"
            ),
        ]

        if artifacts.get("root_cause"):
            sections.append(f"# Root cause\n{artifacts['root_cause']}")
        if artifacts.get("review_summary"):
            sections.append(f"# Review verdict\n{artifacts['review_summary']}")
        if artifacts.get("follow_up"):
            sections.append(
                "# Deliberately left out\n"
                + "\n".join(f"- {item}" for item in artifacts["follow_up"])
            )

        sections.append(
            "Write for the reviewer who has to approve this at the end of their day: "
            "what changed, why it was necessary, and what evidence says it works. "
            "State what was verified and what was not -- do not describe a passing test "
            "run that did not happen. Update documentation only where this change "
            "actually made it wrong."
        )
        return "\n\n".join(sections)

    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        if not result.ok:
            return {
                "errors": [f"documentation: {result.error}"],
                "history": [result.to_history()],
            }

        payload = result.payload
        # Documentation is the last node in every workflow it appears in, so it
        # settles the run -- except where something upstream already failed or
        # is waiting on a person, which it must not paper over.
        current = state.get("status", Status.REVIEWING.value)
        terminal = {Status.FAILED.value, Status.AWAITING_HUMAN.value}
        return {
            "status": current if current in terminal else Status.SUCCESS.value,
            "history": [result.to_history()],
            "artifacts": {
                "pr_title": payload.get("pr_title", ""),
                "pr_body": payload.get("pr_body", ""),
                "docs_updated": payload.get("docs_updated") or [],
                "release_note": payload.get("release_note", ""),
            },
        }


__all__ = ["DocumentationAgent"]

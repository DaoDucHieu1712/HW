"""Jira agent -- turns a ticket into a workable objective."""

from __future__ import annotations

from typing import Any

from graph.state import AgentState, Status

from .base import AgentConfig, AgentResult, BaseAgent


class JiraAgent(BaseAgent):
    """Reads the ticket and states what actually has to be true when it is done.

    Tickets are written by people under time pressure: the title is often the
    symptom, the reproduction steps are in a comment, and the acceptance
    criteria are implied. This agent's job is to make all of that explicit
    before any code is written.
    """

    name = "jira"
    description = (
        "Fetches a Jira ticket and extracts the objective, reproduction steps, "
        "acceptance criteria and anything still missing."
    )
    default_config = AgentConfig(
        tools=["query_jira"],
        skills=[],
        max_tokens=6000,
        max_tool_iterations=4,
        effort="medium",
    )
    output_schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string", "description": "The ticket in one paragraph."},
            "objective": {
                "type": "string",
                "description": "What must be true when this is done, stated as an outcome.",
            },
            "issue_key": {"type": "string"},
            "issue_type": {"type": "string"},
            "reproduction_steps": {"type": "array", "items": {"type": "string"}},
            "acceptance_criteria": {"type": "array", "items": {"type": "string"}},
            "components": {"type": "array", "items": {"type": "string"}},
            "correlation_id": {
                "type": "string",
                "description": "Any trace or correlation id mentioned in the ticket.",
            },
            "missing_information": {
                "type": "array",
                "items": {"type": "string"},
                "description": "What the ticket does not say but the work needs.",
            },
        },
        "required": ["summary", "objective"],
        "additionalProperties": True,
    }

    def build_prompt(self, state: AgentState) -> str:
        context = state.get("context") or {}
        key = context.get("issue_key") or state.get("task", "")
        sections = [
            f"# Ticket\n{key}",
            "Fetch it with query_jira, then read the description *and* the comments: "
            "the real reproduction steps are often in a comment rather than the body.",
        ]
        if context.get("notes"):
            sections.append(f"# Additional notes from the requester\n{context['notes']}")
        sections.append(
            "State the objective as an outcome, not as a task list. Separate what the "
            "ticket actually says from what you are inferring, and list anything the "
            "work needs that the ticket does not provide."
        )
        return "\n\n".join(sections)

    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        if not result.ok:
            return {
                "status": Status.FAILED.value,
                "errors": [f"jira: {result.error}"],
                "history": [result.to_history()],
            }

        payload = result.payload
        context_patch: dict[str, Any] = {
            "ticket_summary": payload.get("summary", ""),
            "issue_key": payload.get("issue_key", ""),
            "issue_type": payload.get("issue_type", ""),
            "reproduction_steps": payload.get("reproduction_steps") or [],
            "acceptance_criteria": payload.get("acceptance_criteria") or [],
            "components": payload.get("components") or [],
        }
        if payload.get("correlation_id"):
            context_patch["correlation_id"] = payload["correlation_id"]

        return {
            "objective": payload.get("objective") or state.get("objective", ""),
            "context": context_patch,
            "status": Status.PLANNING.value,
            "history": [result.to_history()],
            "artifacts": {"missing_information": payload.get("missing_information") or []},
        }


__all__ = ["JiraAgent"]

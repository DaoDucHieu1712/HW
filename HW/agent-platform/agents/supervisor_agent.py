"""Supervisor agent -- routes work between the specialist agents.

The supervisor exists for the cases a fixed edge cannot express: an ambiguous
request, a run that has stalled, a decision about whether another iteration is
worth spending. Deterministic workflows (bug fix, code review) use the graph's
own edges; the supervisor is what an open-ended request routes through.
"""

from __future__ import annotations

from typing import Any

from graph.state import AgentState, Status, summarise

from .base import AgentConfig, AgentResult, BaseAgent

#: The agents the supervisor is allowed to route to.
ROUTABLE = (
    "jira",
    "planner",
    "coding",
    "unittest",
    "review",
    "log_analysis",
    "documentation",
)


class SupervisorAgent(BaseAgent):
    """Decides which agent acts next, or that the run is done."""

    name = "supervisor"
    description = (
        "Chooses the next agent for the current state of the run, or ends it. "
        "Routes on evidence rather than on optimism."
    )
    default_config = AgentConfig(
        tools=[],  # The supervisor reasons over state; it does not act on the world.
        skills=[],
        max_tokens=4000,
        max_tool_iterations=1,
        effort="medium",
    )
    output_schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string", "description": "Why this is the right next step."},
            "next_agent": {
                "type": "string",
                "enum": [*ROUTABLE, "done"],
                "description": "The agent to run next, or 'done' to finish the run.",
            },
            "reason": {"type": "string"},
            "needs_human": {
                "type": "boolean",
                "description": "True when the run cannot proceed without a person.",
            },
            "question_for_human": {"type": "string"},
        },
        "required": ["summary", "next_agent"],
        "additionalProperties": True,
    }

    def build_prompt(self, state: AgentState) -> str:
        sections = [
            f"# Objective\n{state.get('objective') or state.get('task')}",
            f"# Current state\n{summarise(state)}",
            "# What has happened so far\n"
            + (
                "\n".join(
                    f"{index}. {entry.get('agent')}: "
                    f"{'ok' if entry.get('ok') else 'FAILED'} -- {entry.get('summary', '')}"
                    for index, entry in enumerate(state.get("history") or [], 1)
                )
                or "(nothing yet)"
            ),
            "# Agents you can route to\n"
            "- jira: read the ticket and state the objective\n"
            "- planner: investigate and produce a plan\n"
            "- coding: implement the plan and build\n"
            "- unittest: write and run tests\n"
            "- review: judge correctness and risk\n"
            "- log_analysis: reconstruct an incident from logs\n"
            "- documentation: write the PR description\n"
            "- done: the objective is met, or nothing further can be done",
        ]

        errors = state.get("errors") or []
        if errors:
            sections.append("# Recent errors\n" + "\n".join(f"- {e}" for e in errors[-5:]))

        iteration = state.get("iteration", 0)
        limit = state.get("max_iterations", 3)
        if iteration >= limit:
            sections.append(
                f"# Iteration budget is spent ({iteration}/{limit})\n"
                "Do not start another cycle. Either finish, or escalate to a human "
                "with a clear statement of what is unresolved."
            )

        sections.append(
            "Pick the one step that moves the run forward. Repeating an agent that "
            "just failed the same way will fail the same way again -- change something, "
            "or escalate. If the evidence says the objective is met, answer 'done'."
        )
        return "\n\n".join(sections)

    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        if not result.ok:
            # A supervisor that cannot decide must not silently pick something.
            return {
                "status": Status.FAILED.value,
                "errors": [f"supervisor: {result.error}"],
                "history": [result.to_history()],
            }

        payload = result.payload
        target = str(payload.get("next_agent", "done")).strip().lower()
        if target not in {*ROUTABLE, "done"}:
            target = "done"

        patch: dict[str, Any] = {
            "next_agent": target,
            "history": [result.to_history()],
            "artifacts": {"routing_reason": payload.get("reason", "")},
        }
        if payload.get("needs_human"):
            patch["status"] = Status.AWAITING_HUMAN.value
            patch["artifacts"]["question_for_human"] = payload.get("question_for_human", "")
        elif target == "done":
            patch["status"] = (
                Status.SUCCESS.value
                if state.get("status") not in {Status.FAILED.value}
                else Status.FAILED.value
            )
        return patch


__all__ = ["ROUTABLE", "SupervisorAgent"]

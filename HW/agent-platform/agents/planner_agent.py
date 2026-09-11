"""Planner agent -- turns an objective into an ordered, checkable plan."""

from __future__ import annotations

from typing import Any

from graph.state import AgentState, Status

from .base import AgentConfig, AgentResult, BaseAgent


class PlannerAgent(BaseAgent):
    """Investigates first, then plans.

    The planner is allowed read-only tools on purpose: a plan written without
    looking at the code is a guess, and the rest of the graph will spend its
    iterations discovering that.
    """

    name = "planner"
    description = (
        "Breaks an objective into a small ordered plan of concrete steps, naming "
        "the files to change and how the result will be verified."
    )
    default_config = AgentConfig(
        tools=["search_code", "read_file", "query_jira", "analyze_logs"],
        skills=["clean_architecture", "cqrs"],
        max_tokens=8000,
        max_tool_iterations=8,
    )
    output_schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string", "description": "One paragraph on the approach."},
            "root_cause": {
                "type": "string",
                "description": "For a bug: the mechanism, not the symptom. Empty when not applicable.",
            },
            "plan": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Ordered steps, each one concrete enough to act on.",
            },
            "files_to_change": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Workspace-relative paths expected to change.",
            },
            "verification": {
                "type": "string",
                "description": "How success will be proven -- the test or command to run.",
            },
            "open_questions": {
                "type": "array",
                "items": {"type": "string"},
                "description": "What could not be settled from the code and needs a human.",
            },
        },
        "required": ["summary", "plan", "verification"],
        "additionalProperties": True,
    }

    def build_prompt(self, state: AgentState) -> str:
        sections = [
            f"# Objective\n{state.get('objective') or state.get('task')}",
            f"# Task\n{state.get('task', '')}",
        ]

        context = state.get("context") or {}
        if context:
            sections.append("# Supplied context\n" + _render_context(context))

        # A re-plan is a different job from a first plan: say what failed.
        if state.get("iteration", 0) > 0:
            sections.append(
                f"# This is revision {state['iteration']}\n"
                "The previous attempt did not pass verification. Do not repeat it -- "
                "work out why it failed and plan around that cause."
            )
            failures = _recent_failures(state)
            if failures:
                sections.append("# What failed last time\n" + failures)

        blocking = state.get("review_comments") or []
        if blocking:
            sections.append(
                "# Outstanding review findings\n"
                + "\n".join(
                    f"- [{c.get('severity', 'minor')}] {c.get('path', '')}: {c.get('message', '')}"
                    for c in blocking[:15]
                )
            )

        sections.append(
            "Investigate before planning: search the code and read what you need. "
            "Keep the plan as short as the work actually is -- three real steps beat "
            "ten plausible ones. Every step must be something a developer could do "
            "and check."
        )
        return "\n\n".join(sections)

    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        if not result.ok:
            return {
                "status": Status.FAILED.value,
                "errors": [f"planner: {result.error}"],
                "history": [result.to_history()],
            }

        payload = result.payload
        patch: dict[str, Any] = {
            "plan": list(payload.get("plan") or []),
            "status": Status.IN_PROGRESS.value,
            "history": [result.to_history()],
            "artifacts": {
                "plan_summary": payload.get("summary", ""),
                "verification": payload.get("verification", ""),
                "files_to_change": payload.get("files_to_change") or [],
            },
        }
        if payload.get("root_cause"):
            patch["artifacts"]["root_cause"] = payload["root_cause"]
            # A root cause is durable knowledge about this codebase.
            self.memory.learn(
                f"Root cause of {state.get('task', 'the reported issue')}: {payload['root_cause']}",
                kind="root_cause",
                workflow=state.get("workflow", ""),
            )
        if payload.get("open_questions"):
            patch["artifacts"]["open_questions"] = payload["open_questions"]
        return patch


def _render_context(context: dict[str, Any]) -> str:
    lines: list[str] = []
    for key, value in context.items():
        text = value if isinstance(value, str) else str(value)
        lines.append(f"## {key}\n{text[:4000]}")
    return "\n\n".join(lines)


def _recent_failures(state: AgentState, limit: int = 2) -> str:
    parts: list[str] = []
    for result in (state.get("test_results") or [])[-limit:]:
        if not result.get("success"):
            parts.append(
                f"- {result.get('kind', 'run')} failed: {str(result.get('output', ''))[-1500:]}"
            )
    parts.extend(f"- {error}" for error in (state.get("errors") or [])[-limit:])
    return "\n".join(parts)


__all__ = ["PlannerAgent"]

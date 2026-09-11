"""Log analysis agent -- reconstructs an incident timeline from evidence."""

from __future__ import annotations

from typing import Any

from graph.state import AgentState, Status

from .base import AgentConfig, AgentResult, BaseAgent


class LogAnalysisAgent(BaseAgent):
    """Reads logs and separates cause from consequence.

    The output schema forces a quote for the root cause: a claim about what
    happened must be traceable to a line someone can go and read.
    """

    name = "log_analysis"
    description = (
        "Reconstructs what happened from error logs and traces: the ordered "
        "timeline, the first real failure, and the evidence for it."
    )
    default_config = AgentConfig(
        tools=["analyze_logs", "search_code", "query_database", "read_file"],
        skills=["mes", "event_driven_architecture"],
        max_tokens=12000,
        max_tool_iterations=10,
    )
    output_schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string", "description": "What happened, in plain language."},
            "root_cause": {
                "type": "string",
                "description": "The first real failure -- the mechanism, not the last error seen.",
            },
            "evidence": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Quoted log lines that support the conclusion, in order.",
            },
            "timeline": {
                "type": "array",
                "description": "Ordered events, oldest first.",
                "items": {
                    "type": "object",
                    "properties": {
                        "timestamp": {"type": "string"},
                        "event": {"type": "string"},
                        "role": {
                            "type": "string",
                            "enum": ["cause", "consequence", "context"],
                        },
                    },
                    "required": ["event", "role"],
                },
            },
            "affected_components": {"type": "array", "items": {"type": "string"}},
            "confidence": {"type": "string", "enum": ["low", "medium", "high"]},
            "recommended_action": {"type": "string"},
        },
        "required": ["summary", "root_cause", "evidence", "confidence"],
        "additionalProperties": True,
    }

    def build_prompt(self, state: AgentState) -> str:
        context = state.get("context") or {}
        sections = [f"# Incident\n{state.get('task', '')}"]

        if context.get("correlation_id"):
            sections.append(
                f"# Correlation id\n{context['correlation_id']}\n\n"
                "Pull this request's whole timeline first, then read it in order."
            )
        if context.get("stack_trace"):
            sections.append(f"# Reported stack trace\n```\n{context['stack_trace'][:6000]}\n```")
        if context.get("time_window_minutes"):
            sections.append(f"# Time window\n{context['time_window_minutes']} minutes")

        sections.append(
            "Read the *original* exception, not the wrapper. The last error in a log "
            "is usually a consequence of the first one -- order the events before "
            "concluding anything. Quote the lines you rely on, verbatim, so the "
            "conclusion can be checked. If the evidence does not settle it, say so "
            "and report low confidence rather than guessing a cause."
        )
        return "\n\n".join(sections)

    def memory_query(self, state: AgentState) -> str:
        context = state.get("context") or {}
        return f"{state.get('task', '')} {context.get('component', '')}".strip()

    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        if not result.ok:
            return {
                "status": Status.FAILED.value,
                "errors": [f"log_analysis: {result.error}"],
                "history": [result.to_history()],
            }

        payload = result.payload
        timeline = payload.get("timeline") or []
        patch: dict[str, Any] = {
            "logs": [str(item) for item in (payload.get("evidence") or [])],
            "status": Status.REVIEWING.value,
            "history": [result.to_history()],
            "artifacts": {
                "root_cause": payload.get("root_cause", ""),
                "timeline": timeline,
                "confidence": payload.get("confidence", "low"),
                "affected_components": payload.get("affected_components") or [],
                "recommended_action": payload.get("recommended_action", ""),
            },
        }
        # Only record a finding as project knowledge when it is well supported.
        if payload.get("confidence") == "high" and payload.get("root_cause"):
            self.memory.learn(
                f"Incident '{state.get('task', '')}' root cause: {payload['root_cause']}",
                kind="incident",
                workflow=state.get("workflow", ""),
            )
        return patch


__all__ = ["LogAnalysisAgent"]

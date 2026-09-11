"""Log analysis workflow.

    START -> analyse -> report -> END

Read-only and single-pass. It answers "what happened", it does not change
anything. When the analysis points at a fix, the answer is to start a bug fix
run with the correlation id it found -- not to let the diagnostic workflow
quietly grow a write path.
"""

from __future__ import annotations

from typing import Any

from graph.builder import GraphSpec
from graph.edges import FINISH
from graph.nodes import agent_node
from graph.state import AgentState

from .base import Workflow


class LogAnalysisWorkflow(Workflow):
    """Reconstructs an incident timeline and reports the root cause."""

    name = "analyzelog"
    description = (
        "Reconstructs what happened from logs and traces, separates cause from "
        "consequence, and writes up the timeline with quoted evidence."
    )
    required_arguments = ()

    def build_spec(self) -> GraphSpec:
        agents = self.context.agents
        spec = GraphSpec(name=self.name)
        spec.add_node("analyse", agent_node(agents.get("log_analysis")))
        spec.add_node("report", agent_node(agents.get("documentation")))
        spec.set_entry("analyse")
        spec.add_edge("analyse", "report")
        spec.add_edge("report", FINISH)
        return spec

    def seed_state(self, **arguments: Any) -> AgentState:
        correlation_id = arguments.get("correlation_id")
        query = arguments.get("query")
        if not correlation_id and not query:
            raise ValueError("provide either correlation_id or query")

        subject = f"correlation id {correlation_id}" if correlation_id else f"'{query}'"
        context: dict[str, Any] = {
            "time_window_minutes": arguments.get("since_minutes") or 60,
            # Diagnostic only: there is no change to build or test.
            "verification_required": False,
        }
        for key in ("correlation_id", "query", "stack_trace", "component", "level"):
            if arguments.get(key):
                context[key] = arguments[key]

        return self.new_state(
            f"Analyse the incident for {subject}",
            objective=(
                f"Establish what happened for {subject}: the ordered timeline, the "
                "first real failure, and the evidence for it."
            ),
            context=context,
            max_iterations=1,
        )


__all__ = ["LogAnalysisWorkflow"]

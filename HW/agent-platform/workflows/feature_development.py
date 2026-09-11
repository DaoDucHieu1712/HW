"""Feature development workflow.

    START -> planner -> coding -> unittest -> plugins -> review -> deliver -> END
                 ^          |          |                   |
                 +- iterate + (red) ---+------------- (not approved)

The same loop as the bug fix, minus the ticket-and-logs triage: a new feature
starts from a requirement rather than from a failure. Human-in-the-loop matters
more here -- a feature involves product decisions a platform should not make on
its own -- so the ``human`` node is wired in and pauses when enabled.
"""

from __future__ import annotations

from typing import Any

from graph.builder import GraphSpec
from graph.edges import (
    FINISH,
    route_after_build,
    route_after_human,
    route_after_iteration,
    route_after_tests,
    route_review_outcome,
)
from graph.nodes import agent_node, human_review_node, iteration_node, plugin_node
from graph.state import AgentState

from .base import Workflow


class FeatureDevelopmentWorkflow(Workflow):
    """Builds a new capability from a requirement, verified and documented."""

    name = "feature"
    description = (
        "Plans, implements, tests, reviews and documents a new feature as a "
        "vertical slice."
    )
    required_arguments = ("requirement",)

    def build_spec(self) -> GraphSpec:
        agents = self.context.agents
        spec = GraphSpec(name=self.name)

        spec.add_node("planner", agent_node(agents.get("planner")))
        spec.add_node("coding", agent_node(agents.get("coding")))
        spec.add_node("unittest", agent_node(agents.get("unittest")))
        spec.add_node("plugins", plugin_node(self.context.plugins, "review"))
        spec.add_node("review", agent_node(agents.get("review")))
        spec.add_node("deliver", agent_node(agents.get("documentation")))
        spec.add_node("iterate", iteration_node)
        spec.add_node("human", human_review_node)

        spec.set_entry("planner")
        spec.add_edge("planner", "coding")
        spec.add_edge("unittest", "plugins")
        spec.add_edge("plugins", "review")
        spec.add_edge("deliver", FINISH)

        spec.add_conditional(
            "coding",
            route_after_build,
            {"unittest": "unittest", "iterate": "iterate", FINISH: FINISH},
        )
        spec.add_conditional(
            "unittest",
            route_after_tests,
            {"review": "plugins", "coding": "coding", FINISH: FINISH},
        )
        spec.add_conditional(
            "review",
            route_review_outcome,
            {"deliver": "deliver", "iterate": "iterate", "human": "human", FINISH: FINISH},
        )
        spec.add_conditional(
            "iterate", route_after_iteration, {"planner": "planner", FINISH: FINISH}
        )
        spec.add_conditional(
            "human", route_after_human, {"planner": "planner", FINISH: FINISH}
        )
        return spec

    def seed_state(self, **arguments: Any) -> AgentState:
        requirement = str(arguments["requirement"]).strip()
        context: dict[str, Any] = {"requirement": requirement}
        for key in ("acceptance_criteria", "issue_key", "constraints", "out_of_scope"):
            if arguments.get(key):
                context[key] = arguments[key]

        return self.new_state(
            f"Implement: {requirement[:120]}",
            objective=(
                arguments.get("objective")
                or f"Deliver this as a working, tested vertical slice: {requirement}"
            ),
            context=context,
            max_iterations=arguments.get("max_iterations"),
        )


__all__ = ["FeatureDevelopmentWorkflow"]

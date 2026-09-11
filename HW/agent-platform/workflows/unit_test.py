"""Unit test generation workflow.

    START -> planner -> unittest -> review -> END
                 ^          |
                 +- iterate + (tests red)

The planner runs first so the tests target the right seam. Generating tests
straight from a file name produces coverage of whatever is easy to call, which
is rarely the behaviour that matters.
"""

from __future__ import annotations

from typing import Any

from graph.builder import GraphSpec
from graph.edges import FINISH, route_after_iteration, route_after_tests
from graph.nodes import agent_node, iteration_node
from graph.state import AgentState

from .base import Workflow


class GenerateUnitTestWorkflow(Workflow):
    """Writes and runs tests for existing code."""

    name = "unittest"
    description = (
        "Generates unit tests for a file or symbol, runs them, and reviews what "
        "they actually pin down."
    )
    required_arguments = ("target",)

    def build_spec(self) -> GraphSpec:
        agents = self.context.agents
        spec = GraphSpec(name=self.name)

        spec.add_node("planner", agent_node(agents.get("planner")))
        spec.add_node("unittest", agent_node(agents.get("unittest")))
        spec.add_node("review", agent_node(agents.get("review")))
        spec.add_node("iterate", iteration_node)

        spec.set_entry("planner")
        spec.add_edge("planner", "unittest")
        spec.add_edge("review", FINISH)

        # Red tests go back to the test agent; if the budget is spent, stop.
        spec.add_conditional(
            "unittest",
            route_after_tests,
            {"review": "review", "coding": "iterate", FINISH: FINISH},
        )
        spec.add_conditional(
            "iterate",
            route_after_iteration,
            {"planner": "unittest", FINISH: FINISH},
        )
        return spec

    def seed_state(self, **arguments: Any) -> AgentState:
        target = str(arguments["target"]).strip()
        symbol = arguments.get("symbol")
        framework = arguments.get("framework") or "xunit"
        subject = f"{target}::{symbol}" if symbol else target

        return self.new_state(
            f"Write unit tests for {subject}",
            objective=(
                f"Cover the behaviour of {subject} with {framework} tests: the happy "
                "path, the boundaries, and the failure modes the code actually has. "
                "Each test must fail if the behaviour it names is broken."
            ),
            context={
                "target": target,
                "symbol": symbol or "",
                "framework": framework,
                **({"focus": arguments["focus"]} if arguments.get("focus") else {}),
            },
            max_iterations=arguments.get("max_iterations"),
        )


__all__ = ["GenerateUnitTestWorkflow"]

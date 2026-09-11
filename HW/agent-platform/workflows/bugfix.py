"""Bug fix workflow -- the platform's reference implementation.

The ten steps from the specification map onto the graph like this::

    jira        1. read the ticket
    triage      2. analyse the logs
    planner     3. search the source, 4. produce a fix proposal
    coding      5. implement the fix, 6. build the solution
    unittest    7. generate unit tests, 8. run them
    plugins     (security / performance / architecture passes)
    review      9. review the result
    deliver    10. document the change and open the pull request

    START -> jira -> triage -> planner -> coding -> unittest -> plugins -> review
                                  ^                     |                    |
                                  |                     v                    v
                                  +---- iterate <--- (red) ------------ (not approved)
                                                                            |
                                                                       (approved)
                                                                            v
                                                                        deliver -> END

A red build or a red test sends the run back to ``coding`` rather than to the
planner: the plan is usually still right, and re-planning throws away work that
was already correct. Only a review that rejects the *approach* costs a full
iteration.
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


class BugFixWorkflow(Workflow):
    """End-to-end bug fix: ticket in, reviewed pull request out."""

    name = "bugfix"
    description = (
        "Reads the ticket and the logs, finds the root cause, implements and "
        "verifies a fix, then opens a documented pull request."
    )
    required_arguments = ("issue_key",)

    def build_spec(self) -> GraphSpec:
        agents = self.context.agents
        spec = GraphSpec(name=self.name)

        spec.add_node("jira", agent_node(agents.get("jira")))
        spec.add_node("triage", agent_node(agents.get("log_analysis")))
        spec.add_node("planner", agent_node(agents.get("planner")))
        spec.add_node("coding", agent_node(agents.get("coding")))
        spec.add_node("unittest", agent_node(agents.get("unittest")))
        spec.add_node("plugins", plugin_node(self.context.plugins, "review"))
        spec.add_node("review", agent_node(agents.get("review")))
        spec.add_node("deliver", agent_node(agents.get("documentation")))
        spec.add_node("iterate", iteration_node)
        spec.add_node("human", human_review_node)

        spec.set_entry("jira")
        spec.add_edge("jira", "triage")
        spec.add_edge("triage", "planner")
        spec.add_edge("planner", "coding")
        spec.add_edge("unittest", "plugins")
        spec.add_edge("plugins", "review")
        spec.add_edge("deliver", FINISH)

        # A failed build never reaches the test agent.
        spec.add_conditional(
            "coding",
            route_after_build,
            {"unittest": "unittest", "iterate": "iterate", FINISH: FINISH},
        )
        # Failing tests go back to coding, not to the planner.
        spec.add_conditional(
            "unittest",
            route_after_tests,
            {"review": "plugins", "coding": "coding", FINISH: FINISH},
        )
        spec.add_conditional(
            "review",
            route_review_outcome,
            {
                "deliver": "deliver",
                "iterate": "iterate",
                "human": "human",
                FINISH: FINISH,
            },
        )
        spec.add_conditional(
            "iterate",
            route_after_iteration,
            {"planner": "planner", FINISH: FINISH},
        )
        spec.add_conditional(
            "human",
            route_after_human,
            {"planner": "planner", FINISH: FINISH},
        )
        return spec

    def seed_state(self, **arguments: Any) -> AgentState:
        issue_key = str(arguments["issue_key"]).strip()
        context: dict[str, Any] = {"issue_key": issue_key}
        for key in ("correlation_id", "stack_trace", "notes", "time_window_minutes"):
            if arguments.get(key):
                context[key] = arguments[key]

        return self.new_state(
            f"Fix {issue_key}",
            objective=(
                f"Find and fix the root cause of {issue_key}, prove the fix with a test "
                "that would have failed before it, and open a pull request describing it."
            ),
            context=context,
            max_iterations=arguments.get("max_iterations"),
        )


__all__ = ["BugFixWorkflow"]

"""Code review workflow -- read-only.

    START -> plugins -> review -> END

Deliberately linear and without a loop: this workflow reports, it does not fix.
A review that quietly edits the code it is reviewing leaves no record of what
was wrong, and the person who asked for a review gets a change instead of an
answer.
"""

from __future__ import annotations

from typing import Any

from graph.builder import GraphSpec
from graph.edges import FINISH
from graph.nodes import agent_node, plugin_node
from graph.state import AgentState

from .base import Workflow


class CodeReviewWorkflow(Workflow):
    """Runs the review plugins and the review agent over a set of changes."""

    name = "review"
    description = (
        "Reviews a set of changed files for correctness, security, performance "
        "and architecture. Reports findings; changes nothing."
    )
    required_arguments = ()

    def build_spec(self) -> GraphSpec:
        spec = GraphSpec(name=self.name)
        spec.add_node("plugins", plugin_node(self.context.plugins, "review"))
        spec.add_node("review", agent_node(self.context.agents.get("review")))
        spec.set_entry("plugins")
        spec.add_edge("plugins", "review")
        spec.add_edge("review", FINISH)
        return spec

    def seed_state(self, **arguments: Any) -> AgentState:
        paths: list[str] = list(arguments.get("paths") or [])
        target = arguments.get("target") or "the current working tree"

        state = self.new_state(
            f"Review {target}",
            objective=(
                arguments.get("objective")
                or f"Report the defects in {target}, ranked by severity."
            ),
            context={
                "target": target,
                "paths": paths,
                # Nothing here builds or runs tests, so the reviewer must not
                # withhold its verdict waiting for evidence that cannot exist.
                "verification_required": False,
                **({"diff": arguments["diff"]} if arguments.get("diff") else {}),
            },
            max_iterations=1,  # one pass; a review does not iterate
        )
        # The review agent and the plugins both scope themselves to
        # `code_changes`, so a caller-supplied path list seeds that.
        state["code_changes"] = [
            {"path": path, "action": "modified", "rationale": "under review", "applied": True}
            for path in paths
        ]
        return state


__all__ = ["CodeReviewWorkflow"]

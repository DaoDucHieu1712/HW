"""Graph nodes.

A node is a pure-ish function ``AgentState -> partial AgentState``. Wrapping an
agent in a node is where the agent's result becomes a state patch, and where the
iteration counter and per-node tracing live -- so agents stay unaware of the
graph and the graph stays unaware of prompting.
"""

from __future__ import annotations

import logging
from typing import TYPE_CHECKING, Any, Callable

from configs.telemetry import span

from .state import AgentState, Status

if TYPE_CHECKING:  # imported for typing only -- agents and plugins depend on
    # graph.state, so importing them here at runtime would close the cycle.
    from agents.base import BaseAgent
    from agents.registry import AgentRegistry
    from plugins.manager import PluginManager

logger = logging.getLogger(__name__)

#: Every node has this shape.
Node = Callable[[AgentState], dict[str, Any]]


def agent_node(agent: "BaseAgent") -> Node:
    """Wrap an agent as a graph node."""

    def run(state: AgentState) -> dict[str, Any]:
        with span(f"agent.{agent.name}", agent=agent.name, status=state.get("status")):
            logger.info(
                "-> %s (iteration %s/%s)",
                agent.name,
                state.get("iteration", 0),
                state.get("max_iterations", 0),
            )
            result = agent.run(state)
            patch = agent.apply(state, result)
            if result.usage:
                # Token spend is per-node bookkeeping, not part of the agent's answer.
                artifacts = dict(patch.get("artifacts") or {})
                artifacts[f"usage.{agent.name}"] = result.usage
                patch["artifacts"] = artifacts
            return patch

    run.__name__ = f"node_{agent.name}"
    return run


def make_nodes(agents: "AgentRegistry", names: list[str]) -> dict[str, Node]:
    """Build the node map for a workflow from agent names."""
    return {name: agent_node(agents.get(name)) for name in names}


def iteration_node(state: AgentState) -> dict[str, Any]:
    """Increment the loop counter. Placed on the edge back into the planner.

    Counting in its own node means the budget is enforced by the graph, not by
    whichever agent happens to run next.
    """
    current = int(state.get("iteration", 0)) + 1
    limit = int(state.get("max_iterations", 3))
    patch: dict[str, Any] = {"iteration": current}
    if current > limit:
        patch["status"] = Status.FAILED.value
        patch["errors"] = [f"iteration budget exhausted after {limit} attempt(s)"]
        logger.warning("iteration budget exhausted (%d/%d)", current, limit)
    return patch


def plugin_node(plugins: "PluginManager", stage: str) -> Node:
    """Run every plugin registered for ``stage`` and fold in their findings."""

    def run(state: AgentState) -> dict[str, Any]:
        with span(f"plugins.{stage}", stage=stage):
            findings = plugins.run_stage(stage, state)
        if not findings:
            return {}
        return {
            "review_comments": findings,
            "artifacts": {
                "plugin_findings": [
                    f"[{f.get('severity', 'minor')}] {f.get('source', 'plugin')}: "
                    f"{f.get('message', '')}"
                    for f in findings
                ]
            },
        }

    run.__name__ = f"node_plugins_{stage}"
    return run


def human_review_node(state: AgentState) -> dict[str, Any]:
    """Pause point for human-in-the-loop.

    The node itself does nothing: LangGraph is compiled with
    ``interrupt_before`` on it, so execution stops here and the caller resumes
    the thread once a person has looked. Any feedback they leave in
    ``human_feedback`` is promoted into the state the next agent reads.
    """
    feedback = str(state.get("human_feedback", "")).strip()
    if not feedback:
        return {"status": Status.AWAITING_HUMAN.value}
    logger.info("resuming with human feedback (%d chars)", len(feedback))
    return {
        "context": {"human_feedback": feedback},
        "status": Status.IN_PROGRESS.value,
        "human_feedback": "",
    }


def terminal_node(state: AgentState) -> dict[str, Any]:
    """Normalise the final status so callers see one of two outcomes."""
    status = state.get("status", Status.PENDING.value)
    if status not in {Status.SUCCESS.value, Status.FAILED.value, Status.AWAITING_HUMAN.value}:
        status = Status.FAILED.value
    return {"status": status}


__all__ = [
    "Node",
    "agent_node",
    "human_review_node",
    "iteration_node",
    "make_nodes",
    "plugin_node",
    "terminal_node",
]

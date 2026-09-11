"""Conditional edges -- the routing decisions the graph makes on its own.

Routers read state and return the *name* of the next node. They are deliberately
plain functions with no I/O: routing is the part of the system that must be
deterministic, testable, and identical every time it sees the same state.
"""

from __future__ import annotations

import logging
from typing import Callable

from .state import (
    AgentState,
    Status,
    blocking_comments,
    latest_test_result,
    verification_passed,
)

logger = logging.getLogger(__name__)

#: Sentinel meaning "stop"; the compiler maps it onto LangGraph's END.
FINISH = "__end__"

Router = Callable[[AgentState], str]


def _budget_spent(state: AgentState) -> bool:
    return int(state.get("iteration", 0)) >= int(state.get("max_iterations", 3))


def route_after_build(state: AgentState) -> str:
    """After the coding agent: green build goes forward, red goes back."""
    build = latest_test_result(state, "build")
    if build is not None and not build.get("success", False):
        if _budget_spent(state):
            logger.warning("build still failing and the budget is spent; ending run")
            return FINISH
        return "iterate"
    return "unittest"


def route_after_tests(state: AgentState) -> str:
    """After the test agent: failing tests go back to coding, not to the planner.

    A failing test is usually a defect in the change, not in the plan. Re-planning
    on every red test throws away the work that was already correct.
    """
    tests = latest_test_result(state, "unit")
    if tests is not None and not tests.get("success", False):
        if _budget_spent(state):
            return FINISH
        return "coding"
    return "review"


def route_after_review(state: AgentState) -> str:
    """The main loop decision: finish, or go back around to the planner.

    Success needs both things to be true -- the reviewer approved *and* the
    build and tests are green. An approval on unverified code is not success.
    """
    if state.get("status") == Status.AWAITING_HUMAN.value:
        return "human"

    approved = state.get("status") == Status.SUCCESS.value
    blockers = blocking_comments(state)

    if approved and not blockers and verification_passed(state):
        return FINISH

    if _budget_spent(state):
        logger.warning(
            "review not satisfied after %s iteration(s); ending run",
            state.get("iteration"),
        )
        return FINISH

    return "iterate"


def route_review_outcome(state: AgentState) -> str:
    """Like :func:`route_after_review`, but with a delivery step after success.

    Distinguishes "approved, go and document it" from "out of budget, stop" --
    both of which :func:`route_after_review` collapses into FINISH. A run that
    ran out of iterations must not be written up as a finished change.
    """
    decision = route_after_review(state)
    if decision != FINISH:
        return decision
    return "deliver" if state.get("status") == Status.SUCCESS.value else FINISH


def route_after_iteration(state: AgentState) -> str:
    """After the counter: either the budget ran out, or we plan again."""
    return FINISH if state.get("status") == Status.FAILED.value else "planner"


def route_supervisor(state: AgentState) -> str:
    """Dynamic routing for open-ended runs."""
    target = str(state.get("next_agent", "")).strip().lower()
    if not target or target == "done":
        return FINISH
    if state.get("status") == Status.AWAITING_HUMAN.value:
        return "human"
    if _budget_spent(state):
        return FINISH
    return target


def route_after_human(state: AgentState) -> str:
    """Resume after a human looked at it."""
    return FINISH if state.get("status") == Status.FAILED.value else "planner"


__all__ = [
    "FINISH",
    "Router",
    "route_after_build",
    "route_after_human",
    "route_after_iteration",
    "route_after_review",
    "route_review_outcome",
    "route_after_tests",
    "route_supervisor",
]

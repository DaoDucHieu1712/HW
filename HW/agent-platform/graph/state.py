"""The state object every node reads and writes.

``AgentState`` is a ``TypedDict`` because that is what LangGraph merges between
nodes: a node returns a *partial* state and the graph applies it. Keeping the
shape flat and JSON-serialisable is what makes checkpointing, resuming and
human-in-the-loop review possible.
"""

from __future__ import annotations

import uuid
from enum import Enum
from typing import Annotated, Any, TypedDict


class Status(str, Enum):
    """Where the run is. The router reads this, so the values are a contract."""

    PENDING = "pending"
    PLANNING = "planning"
    IN_PROGRESS = "in_progress"
    BUILDING = "building"
    TESTING = "testing"
    REVIEWING = "reviewing"
    #: The verifier is green and the review passed.
    SUCCESS = "success"
    #: Something is wrong but another iteration could fix it.
    NEEDS_REVISION = "needs_revision"
    #: Out of iterations, or blocked on something the platform cannot do.
    FAILED = "failed"
    #: Waiting for a human decision.
    AWAITING_HUMAN = "awaiting_human"

    def __str__(self) -> str:  # pragma: no cover - trivial
        return self.value


TERMINAL_STATUSES = {Status.SUCCESS, Status.FAILED}


def _append(left: list[Any] | None, right: list[Any] | None) -> list[Any]:
    """Reducer: nodes append to a list rather than replacing it."""
    return list(left or []) + list(right or [])


def _merge(left: dict[str, Any] | None, right: dict[str, Any] | None) -> dict[str, Any]:
    """Reducer: shallow-merge dictionaries contributed by different nodes."""
    return {**(left or {}), **(right or {})}


class CodeChange(TypedDict, total=False):
    """One proposed or applied edit."""

    path: str
    action: str  # created | modified | deleted
    rationale: str
    diff: str
    applied: bool


class ReviewComment(TypedDict, total=False):
    """One finding from the review agent or a review plugin."""

    path: str
    line: int
    severity: str  # blocker | major | minor | nit
    category: str  # correctness | security | performance | architecture | style
    message: str
    suggestion: str
    source: str


class TestResult(TypedDict, total=False):
    """Outcome of a verification run."""

    kind: str  # build | unit | integration
    success: bool
    passed: int
    failed: int
    skipped: int
    output: str
    command: str


class AgentState(TypedDict, total=False):
    """Shared state for the whole graph.

    The nine fields named in the platform specification come first; the rest is
    the operational bookkeeping a real run needs (iteration counters, errors,
    correlation id) and is safe to ignore when reasoning about the flow.
    """

    # -- specification fields -------------------------------------------
    task: str
    objective: str
    context: Annotated[dict[str, Any], _merge]
    plan: list[str]
    code_changes: Annotated[list[CodeChange], _append]
    logs: Annotated[list[str], _append]
    test_results: Annotated[list[TestResult], _append]
    review_comments: Annotated[list[ReviewComment], _append]
    status: str

    # -- operational bookkeeping ----------------------------------------
    workflow: str
    correlation_id: str
    iteration: int
    max_iterations: int
    errors: Annotated[list[str], _append]
    artifacts: Annotated[dict[str, Any], _merge]
    history: Annotated[list[dict[str, Any]], _append]
    next_agent: str
    human_feedback: str


def initial_state(
    task: str,
    *,
    objective: str = "",
    workflow: str = "",
    max_iterations: int = 3,
    context: dict[str, Any] | None = None,
) -> AgentState:
    """Build a fully-populated starting state.

    Every key is initialised so nodes never have to guard against a missing one.
    """
    return AgentState(
        task=task,
        objective=objective or task,
        context=dict(context or {}),
        plan=[],
        code_changes=[],
        logs=[],
        test_results=[],
        review_comments=[],
        status=Status.PENDING.value,
        workflow=workflow,
        correlation_id=uuid.uuid4().hex[:12],
        iteration=0,
        max_iterations=max_iterations,
        errors=[],
        artifacts={},
        history=[],
        next_agent="",
        human_feedback="",
    )


# -- read helpers ---------------------------------------------------------


def latest_test_result(state: AgentState, kind: str | None = None) -> TestResult | None:
    """Most recent verification result, optionally of one kind."""
    for result in reversed(state.get("test_results") or []):
        if kind is None or result.get("kind") == kind:
            return result
    return None


def verification_passed(state: AgentState) -> bool:
    """True only when the newest build *and* test runs both succeeded.

    Absence of evidence is not success: a run with no results has not passed.
    """
    build = latest_test_result(state, "build")
    tests = latest_test_result(state, "unit")
    if build is None and tests is None:
        return False
    return all(result.get("success", False) for result in (build, tests) if result is not None)


def blocking_comments(state: AgentState) -> list[ReviewComment]:
    """Review findings severe enough to send the run back around."""
    return [
        comment
        for comment in (state.get("review_comments") or [])
        if str(comment.get("severity", "")).lower() in {"blocker", "major"}
    ]


def summarise(state: AgentState) -> str:
    """One-paragraph human summary of where the run got to."""
    tests = latest_test_result(state, "unit")
    return (
        f"[{state.get('workflow', 'run')}::{state.get('correlation_id', '')}] "
        f"status={state.get('status')} iteration={state.get('iteration')}/"
        f"{state.get('max_iterations')} "
        f"changes={len(state.get('code_changes') or [])} "
        f"findings={len(state.get('review_comments') or [])} "
        f"tests={'n/a' if tests is None else ('pass' if tests.get('success') else 'FAIL')}"
    )


__all__ = [
    "AgentState",
    "CodeChange",
    "ReviewComment",
    "Status",
    "TERMINAL_STATUSES",
    "TestResult",
    "blocking_comments",
    "initial_state",
    "latest_test_result",
    "summarise",
    "verification_passed",
]

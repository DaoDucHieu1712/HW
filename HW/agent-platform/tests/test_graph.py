"""Graph routing, state reducers and the built-in runtime.

Routing is the part of the platform that must be deterministic, so it is tested
directly on hand-built states rather than through a model.
"""

from __future__ import annotations

import pytest

from graph.builder import GraphSpec, SimpleGraph, merge_state
from graph.edges import (
    FINISH,
    route_after_build,
    route_after_iteration,
    route_after_review,
    route_after_tests,
    route_review_outcome,
    route_supervisor,
)
from graph.nodes import iteration_node
from graph.state import (
    AgentState,
    Status,
    blocking_comments,
    initial_state,
    latest_test_result,
    verification_passed,
)


def _state(**overrides) -> AgentState:
    state = initial_state("task", workflow="test", max_iterations=3)
    state.update(overrides)  # type: ignore[typeddict-item]
    return state


def _passed(kind: str) -> dict:
    return {"kind": kind, "success": True}


def _failed(kind: str) -> dict:
    return {"kind": kind, "success": False, "output": "boom"}


class TestStateHelpers:
    def test_no_evidence_is_not_a_pass(self) -> None:
        # Absence of evidence must never read as success.
        assert verification_passed(_state()) is False

    def test_both_build_and_tests_must_be_green(self) -> None:
        assert verification_passed(_state(test_results=[_passed("build"), _failed("unit")])) is False
        assert verification_passed(_state(test_results=[_passed("build"), _passed("unit")])) is True

    def test_only_the_latest_result_of_a_kind_counts(self) -> None:
        state = _state(test_results=[_failed("unit"), _passed("unit")])

        assert latest_test_result(state, "unit") == _passed("unit")
        assert verification_passed(state) is True

    def test_blocking_comments_ignores_nits(self) -> None:
        state = _state(
            review_comments=[
                {"severity": "nit", "message": "naming"},
                {"severity": "blocker", "message": "null dereference"},
                {"severity": "minor", "message": "unused using"},
            ]
        )

        assert [c["severity"] for c in blocking_comments(state)] == ["blocker"]


class TestRouting:
    def test_a_failed_build_never_reaches_the_test_agent(self) -> None:
        assert route_after_build(_state(test_results=[_failed("build")])) == "iterate"

    def test_a_green_build_goes_to_the_test_agent(self) -> None:
        assert route_after_build(_state(test_results=[_passed("build")])) == "unittest"

    def test_a_failed_build_out_of_budget_ends_the_run(self) -> None:
        state = _state(test_results=[_failed("build")], iteration=3, max_iterations=3)

        assert route_after_build(state) == FINISH

    def test_failing_tests_go_back_to_coding_not_to_the_planner(self) -> None:
        # Re-planning on a red test throws away work that was already correct.
        assert route_after_tests(_state(test_results=[_failed("unit")])) == "coding"

    def test_passing_tests_go_to_review(self) -> None:
        assert route_after_tests(_state(test_results=[_passed("unit")])) == "review"

    def test_approval_without_green_verification_is_not_success(self) -> None:
        state = _state(status=Status.SUCCESS.value)  # approved, but nothing ran

        assert route_after_review(state) == "iterate"

    def test_approved_and_verified_finishes(self) -> None:
        state = _state(
            status=Status.SUCCESS.value,
            test_results=[_passed("build"), _passed("unit")],
        )

        assert route_after_review(state) == FINISH
        assert route_review_outcome(state) == "deliver"

    def test_a_blocking_finding_sends_the_run_back(self) -> None:
        state = _state(
            status=Status.SUCCESS.value,
            test_results=[_passed("build"), _passed("unit")],
            review_comments=[{"severity": "blocker", "message": "sql injection"}],
        )

        assert route_after_review(state) == "iterate"

    def test_running_out_of_budget_ends_rather_than_delivering(self) -> None:
        state = _state(status=Status.NEEDS_REVISION.value, iteration=3, max_iterations=3)

        # It must not be written up as a finished change.
        assert route_review_outcome(state) == FINISH

    def test_awaiting_human_routes_to_the_human_node(self) -> None:
        assert route_after_review(_state(status=Status.AWAITING_HUMAN.value)) == "human"

    def test_the_supervisor_routes_to_the_named_agent(self) -> None:
        assert route_supervisor(_state(next_agent="coding")) == "coding"
        assert route_supervisor(_state(next_agent="done")) == FINISH
        assert route_supervisor(_state(next_agent="")) == FINISH


class TestIterationBudget:
    def test_the_counter_increments(self) -> None:
        assert iteration_node(_state(iteration=0))["iteration"] == 1

    def test_exceeding_the_budget_fails_the_run(self) -> None:
        patch = iteration_node(_state(iteration=3, max_iterations=3))

        assert patch["iteration"] == 4
        assert patch["status"] == Status.FAILED.value
        assert "budget exhausted" in patch["errors"][0]

    def test_routing_after_the_counter_respects_the_failure(self) -> None:
        assert route_after_iteration(_state(status=Status.FAILED.value)) == FINISH
        assert route_after_iteration(_state(status=Status.IN_PROGRESS.value)) == "planner"


class TestStateReducers:
    def test_lists_accumulate_across_nodes(self) -> None:
        state = merge_state(_state(errors=["first"]), {"errors": ["second"]})

        assert state["errors"] == ["first", "second"]

    def test_dictionaries_merge_rather_than_replace(self) -> None:
        state = merge_state(_state(context={"a": 1}), {"context": {"b": 2}})

        assert state["context"] == {"a": 1, "b": 2}

    def test_scalars_are_replaced(self) -> None:
        assert merge_state(_state(status="pending"), {"status": "success"})["status"] == "success"


class TestSimpleGraph:
    def test_runs_nodes_in_declared_order(self) -> None:
        visited: list[str] = []
        spec = GraphSpec(name="t")
        spec.add_node("a", lambda s: (visited.append("a"), {"status": "a"})[1])
        spec.add_node("b", lambda s: (visited.append("b"), {"status": "b"})[1])
        spec.set_entry("a")
        spec.add_edge("a", "b")
        spec.add_edge("b", FINISH)

        final = SimpleGraph(spec).invoke(_state())

        assert visited == ["a", "b"]
        assert final["status"] == "b"

    def test_follows_a_conditional_edge(self) -> None:
        spec = GraphSpec(name="t")
        spec.add_node("start", lambda s: {"status": "in_progress"})
        spec.add_node("left", lambda s: {"status": "left"})
        spec.add_node("right", lambda s: {"status": "right"})
        spec.set_entry("start")
        spec.add_conditional(
            "start", lambda s: "right", {"left": "left", "right": "right"}
        )
        spec.add_edge("left", FINISH)
        spec.add_edge("right", FINISH)

        assert SimpleGraph(spec).invoke(_state())["status"] == "right"

    def test_stops_rather_than_looping_forever(self) -> None:
        spec = GraphSpec(name="loop")
        spec.add_node("a", lambda s: {})
        spec.set_entry("a")
        spec.add_edge("a", "a")  # a deliberate infinite loop

        final = SimpleGraph(spec).invoke(_state())

        assert final["status"] == Status.FAILED.value
        assert "step limit" in final["errors"][-1]

    def test_pauses_before_an_interrupt_node(self) -> None:
        spec = GraphSpec(name="hitl")
        spec.add_node("a", lambda s: {"status": "in_progress"})
        spec.add_node("human", lambda s: {"status": "resumed"})
        spec.set_entry("a")
        spec.add_edge("a", "human")
        spec.add_edge("human", FINISH)
        spec.interrupt_before = ["human"]

        final = SimpleGraph(spec).invoke(_state())

        assert final["status"] == Status.AWAITING_HUMAN.value
        assert final["next_agent"] == "human"

    def test_validation_rejects_an_edge_to_an_unknown_node(self) -> None:
        spec = GraphSpec(name="bad")
        spec.add_node("a", lambda s: {})
        spec.set_entry("a")
        spec.add_edge("a", "ghost")

        with pytest.raises(ValueError, match="unknown node"):
            spec.validate()

    def test_validation_rejects_a_missing_entry(self) -> None:
        with pytest.raises(ValueError, match="no entry node"):
            GraphSpec(name="bad").validate()

    def test_mermaid_output_names_every_node(self) -> None:
        spec = GraphSpec(name="t")
        spec.add_node("a", lambda s: {})
        spec.add_node("b", lambda s: {})
        spec.set_entry("a")
        spec.add_edge("a", "b")
        spec.add_edge("b", FINISH)

        diagram = spec.to_mermaid()

        assert "a --> b" in diagram
        assert "END" in diagram

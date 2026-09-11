"""Workflow contract.

A workflow is a named graph plus the arguments that seed its state. It owns the
*shape* of a job -- which agents run, in what order, and what sends the run back
around -- and nothing else: prompting belongs to agents, routing to
``graph/edges.py``, capability to tools.
"""

from __future__ import annotations

import abc
import logging
import time
from dataclasses import dataclass, field
from typing import Any

from agents.registry import AgentRegistry
from configs.settings import GraphSettings
from configs.telemetry import span
from graph.builder import CompiledGraph, GraphSpec, compile_graph
from graph.checkpoint import thread_config
from graph.state import AgentState, Status, initial_state, summarise
from plugins.manager import PluginManager

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class WorkflowResult:
    """Outcome of one workflow run."""

    workflow: str
    status: str
    state: AgentState
    duration_seconds: float = 0.0
    error: str | None = None

    @property
    def succeeded(self) -> bool:
        return self.status == Status.SUCCESS.value

    @property
    def needs_human(self) -> bool:
        return self.status == Status.AWAITING_HUMAN.value

    def summary(self) -> str:
        return summarise(self.state)

    def to_dict(self) -> dict[str, Any]:
        artifacts = self.state.get("artifacts") or {}
        return {
            "workflow": self.workflow,
            "status": self.status,
            "succeeded": self.succeeded,
            "duration_seconds": round(self.duration_seconds, 2),
            "correlation_id": self.state.get("correlation_id"),
            "iterations": self.state.get("iteration"),
            "changes": self.state.get("code_changes") or [],
            "findings": self.state.get("review_comments") or [],
            "test_results": self.state.get("test_results") or [],
            "errors": self.state.get("errors") or [],
            "artifacts": {
                key: value
                for key, value in artifacts.items()
                if not key.startswith("usage.")
            },
            "error": self.error,
        }


@dataclass(slots=True)
class WorkflowContext:
    """Everything a workflow needs from the container."""

    agents: AgentRegistry
    plugins: PluginManager
    graph_settings: GraphSettings
    extras: dict[str, Any] = field(default_factory=dict)


class Workflow(abc.ABC):
    """Base class for the platform's workflows."""

    #: Registry key; also the name a command maps onto.
    name: str = ""
    description: str = ""
    #: Arguments :meth:`run` requires, used for CLI validation and help.
    required_arguments: tuple[str, ...] = ()

    def __init__(self, context: WorkflowContext) -> None:
        self.context = context
        self._graph: CompiledGraph | None = None

    # -- shape -----------------------------------------------------------

    @abc.abstractmethod
    def build_spec(self) -> GraphSpec:
        """Declare the graph for this workflow."""

    @abc.abstractmethod
    def seed_state(self, **arguments: Any) -> AgentState:
        """Build the starting state from the caller's arguments."""

    def new_state(self, task: str, **kwargs: Any) -> AgentState:
        """Helper for :meth:`seed_state` that applies the workflow's defaults."""
        return initial_state(
            task,
            workflow=self.name,
            max_iterations=kwargs.pop("max_iterations", None)
            or self.context.graph_settings.max_iterations,
            **kwargs,
        )

    @property
    def graph(self) -> CompiledGraph:
        """Compiled graph, built once per workflow instance."""
        if self._graph is None:
            spec = self.build_spec()
            if self.context.graph_settings.human_in_the_loop and "human" in spec.nodes:
                spec.interrupt_before = list({*spec.interrupt_before, "human"})
            self._graph = compile_graph(spec, self.context.graph_settings)
        return self._graph

    # -- execution -------------------------------------------------------

    def run(self, **arguments: Any) -> WorkflowResult:
        """Run the workflow to completion (or to a human interrupt)."""
        missing = [name for name in self.required_arguments if not arguments.get(name)]
        if missing:
            raise ValueError(
                f"workflow {self.name!r} requires: {', '.join(missing)}"
            )

        state = self.seed_state(**arguments)
        started = time.perf_counter()
        logger.info(
            "starting workflow %s (correlation_id=%s)", self.name, state.get("correlation_id")
        )

        try:
            with span(f"workflow.{self.name}", workflow=self.name):
                final: AgentState = self.graph.invoke(
                    state, config=thread_config(str(state.get("correlation_id", "")))
                )
        except Exception as exc:  # noqa: BLE001 - a failed run is a reportable outcome
            logger.exception("workflow %s failed", self.name)
            return WorkflowResult(
                workflow=self.name,
                status=Status.FAILED.value,
                state=state,
                duration_seconds=time.perf_counter() - started,
                error=f"{type(exc).__name__}: {exc}",
            )

        result = WorkflowResult(
            workflow=self.name,
            status=str(final.get("status", Status.FAILED.value)),
            state=final,
            duration_seconds=time.perf_counter() - started,
        )
        logger.info("workflow %s finished: %s", self.name, result.summary())
        return result

    def to_mermaid(self) -> str:
        return self.build_spec().to_mermaid()

    def to_dict(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "description": self.description,
            "required_arguments": list(self.required_arguments),
        }

    def __repr__(self) -> str:  # pragma: no cover - debug aid
        return f"<{type(self).__name__} name={self.name!r}>"


__all__ = ["Workflow", "WorkflowContext", "WorkflowResult"]

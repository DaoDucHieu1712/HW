"""Graph construction.

A workflow declares a :class:`GraphSpec` -- nodes, plain edges, conditional
edges -- and the builder compiles it. LangGraph's ``StateGraph`` is the target
when it is installed; :class:`SimpleGraph` is an equivalent in-process runtime
used otherwise, so the platform is testable and demonstrable without the full
dependency tree.

Both runtimes execute the *same* spec, which is what keeps them honest: routing
logic lives in ``graph/edges.py`` and is never duplicated per runtime.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any, Callable, Mapping, Protocol

from configs.settings import GraphSettings

from .checkpoint import build_checkpointer, thread_config
from .edges import FINISH, Router
from .nodes import Node
from .state import AgentState, Status

logger = logging.getLogger(__name__)

START = "__start__"


class CompiledGraph(Protocol):
    """The surface a workflow needs from a compiled graph."""

    def invoke(
        self, state: AgentState, config: Mapping[str, Any] | None = None
    ) -> AgentState:  # pragma: no cover - protocol
        ...


@dataclass(slots=True)
class ConditionalEdge:
    """A router plus the mapping from its return value to a node name."""

    source: str
    router: Router
    #: ``{router result: target node}``. ``FINISH`` terminates the graph.
    targets: dict[str, str]


@dataclass(slots=True)
class GraphSpec:
    """Runtime-neutral description of a workflow graph."""

    name: str
    nodes: dict[str, Node] = field(default_factory=dict)
    entry: str = ""
    edges: list[tuple[str, str]] = field(default_factory=list)
    conditional: list[ConditionalEdge] = field(default_factory=list)
    #: Nodes to pause before, for human-in-the-loop.
    interrupt_before: list[str] = field(default_factory=list)

    # -- assembly --------------------------------------------------------

    def add_node(self, name: str, node: Node) -> "GraphSpec":
        self.nodes[name] = node
        return self

    def add_nodes(self, nodes: Mapping[str, Node]) -> "GraphSpec":
        self.nodes.update(nodes)
        return self

    def set_entry(self, name: str) -> "GraphSpec":
        self.entry = name
        return self

    def add_edge(self, source: str, target: str) -> "GraphSpec":
        self.edges.append((source, target))
        return self

    def add_conditional(
        self, source: str, router: Router, targets: dict[str, str]
    ) -> "GraphSpec":
        self.conditional.append(ConditionalEdge(source, router, targets))
        return self

    # -- validation ------------------------------------------------------

    def validate(self) -> None:
        """Fail loudly at build time rather than mid-run."""
        if not self.entry:
            raise ValueError(f"graph {self.name!r} has no entry node")
        if self.entry not in self.nodes:
            raise ValueError(f"entry node {self.entry!r} is not defined")
        for source, target in self.edges:
            if source not in self.nodes:
                raise ValueError(f"edge from unknown node {source!r}")
            if target != FINISH and target not in self.nodes:
                raise ValueError(f"edge to unknown node {target!r}")
        for edge in self.conditional:
            if edge.source not in self.nodes:
                raise ValueError(f"conditional edge from unknown node {edge.source!r}")
            for target in edge.targets.values():
                if target != FINISH and target not in self.nodes:
                    raise ValueError(f"conditional edge to unknown node {target!r}")

    def to_mermaid(self) -> str:
        """Render the graph for documentation and for ``cli.py graph``."""
        lines = ["graph TD", f"    START([start]) --> {self.entry}"]
        for source, target in self.edges:
            lines.append(
                f"    {source} --> {'END([end])' if target == FINISH else target}"
            )
        for edge in self.conditional:
            for label, target in edge.targets.items():
                arrow = "END([end])" if target == FINISH else target
                lines.append(f"    {edge.source} -->|{label}| {arrow}")
        return "\n".join(lines)


# -- the built-in runtime --------------------------------------------------


class SimpleGraph:
    """Sequential executor for a :class:`GraphSpec`.

    Used when LangGraph is not installed. It applies the same reducer semantics
    the state's ``Annotated`` fields declare -- lists append, dicts merge,
    everything else is replaced -- so behaviour matches the LangGraph runtime.
    """

    #: Hard stop, so a routing bug cannot spin forever.
    MAX_STEPS = 60

    def __init__(self, spec: GraphSpec) -> None:
        spec.validate()
        self.spec = spec

    def invoke(
        self, state: AgentState, config: Mapping[str, Any] | None = None
    ) -> AgentState:
        current: str | None = self.spec.entry
        working: AgentState = dict(state)  # type: ignore[assignment]
        steps = 0

        while current and current != FINISH:
            if steps >= self.MAX_STEPS:
                logger.error("graph %s exceeded %d steps; stopping", self.spec.name, self.MAX_STEPS)
                working["status"] = Status.FAILED.value
                working.setdefault("errors", []).append(  # type: ignore[union-attr]
                    "graph step limit exceeded"
                )
                break
            steps += 1

            if current in self.spec.interrupt_before and not working.get("human_feedback"):
                logger.info("pausing before %s for human review", current)
                working["status"] = Status.AWAITING_HUMAN.value
                working["next_agent"] = current
                break

            node = self.spec.nodes[current]
            patch = node(working) or {}
            working = merge_state(working, patch)
            current = self._next(current, working)

        return working

    def _next(self, node: str, state: AgentState) -> str | None:
        for edge in self.spec.conditional:
            if edge.source == node:
                decision = edge.router(state)
                target = edge.targets.get(decision)
                if target is None:
                    logger.error(
                        "router for %s returned %r, which is not in %s",
                        node,
                        decision,
                        sorted(edge.targets),
                    )
                    return FINISH
                return target
        for source, target in self.spec.edges:
            if source == node:
                return target
        return FINISH


#: State keys whose values accumulate instead of being replaced.
_APPEND_KEYS = {"code_changes", "logs", "test_results", "review_comments", "errors", "history"}
_MERGE_KEYS = {"context", "artifacts"}


def merge_state(state: AgentState, patch: Mapping[str, Any]) -> AgentState:
    """Apply a node's patch using the reducers declared on ``AgentState``."""
    merged: dict[str, Any] = dict(state)
    for key, value in patch.items():
        if key in _APPEND_KEYS:
            merged[key] = list(merged.get(key) or []) + list(value or [])
        elif key in _MERGE_KEYS:
            merged[key] = {**(merged.get(key) or {}), **(value or {})}
        else:
            merged[key] = value
    return merged  # type: ignore[return-value]


# -- compilation -----------------------------------------------------------


def compile_graph(
    spec: GraphSpec,
    settings: GraphSettings | None = None,
    *,
    prefer_langgraph: bool = True,
) -> CompiledGraph:
    """Compile ``spec`` with LangGraph when available, else the built-in runtime."""
    spec.validate()
    if prefer_langgraph:
        compiled = _compile_langgraph(spec, settings)
        if compiled is not None:
            return compiled
    logger.info("graph %s compiled with the built-in runtime", spec.name)
    return SimpleGraph(spec)


def _compile_langgraph(spec: GraphSpec, settings: GraphSettings | None) -> CompiledGraph | None:
    try:
        from langgraph.graph import END, START as LG_START, StateGraph
    except ImportError:
        logger.info("langgraph is not installed; using the built-in graph runtime")
        return None

    builder = StateGraph(AgentState)
    for name, node in spec.nodes.items():
        builder.add_node(name, node)
    builder.add_edge(LG_START, spec.entry)

    for source, target in spec.edges:
        builder.add_edge(source, END if target == FINISH else target)

    for edge in spec.conditional:
        mapping = {
            decision: (END if target == FINISH else target)
            for decision, target in edge.targets.items()
        }
        builder.add_conditional_edges(edge.source, edge.router, mapping)

    interrupts = list(spec.interrupt_before)
    if settings is not None:
        interrupts = list({*interrupts, *settings.interrupt_before})

    compiled = builder.compile(
        checkpointer=build_checkpointer(settings) if settings else None,
        interrupt_before=interrupts or None,
    )
    logger.info("graph %s compiled with LangGraph", spec.name)
    return compiled


__all__ = [
    "CompiledGraph",
    "ConditionalEdge",
    "GraphSpec",
    "START",
    "SimpleGraph",
    "compile_graph",
    "merge_state",
    "thread_config",
]

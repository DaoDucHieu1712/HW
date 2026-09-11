"""Workflow registry.

Workflows are constructed once, per container, from a shared
:class:`workflows.base.WorkflowContext`. Commands and the CLI resolve them by
name, so adding a workflow means registering a class -- not editing a dispatch
table in three places.
"""

from __future__ import annotations

import logging
from typing import Any, Iterator, Type

from .base import Workflow, WorkflowContext
from .bugfix import BugFixWorkflow
from .code_review import CodeReviewWorkflow
from .feature_development import FeatureDevelopmentWorkflow
from .log_analysis import LogAnalysisWorkflow
from .unit_test import GenerateUnitTestWorkflow

logger = logging.getLogger(__name__)

#: Every workflow the platform ships with.
WORKFLOW_TYPES: tuple[Type[Workflow], ...] = (
    BugFixWorkflow,
    CodeReviewWorkflow,
    GenerateUnitTestWorkflow,
    FeatureDevelopmentWorkflow,
    LogAnalysisWorkflow,
)


class WorkflowRegistry:
    """Name-addressed collection of workflows."""

    def __init__(self) -> None:
        self._workflows: dict[str, Workflow] = {}

    def register(self, workflow: Workflow, *, replace: bool = False) -> Workflow:
        if workflow.name in self._workflows and not replace:
            raise ValueError(f"workflow {workflow.name!r} is already registered")
        self._workflows[workflow.name] = workflow
        return workflow

    def get(self, name: str) -> Workflow:
        try:
            return self._workflows[name]
        except KeyError as exc:
            raise KeyError(
                f"unknown workflow {name!r}; registered: {', '.join(sorted(self._workflows))}"
            ) from exc

    def has(self, name: str) -> bool:
        return name in self._workflows

    def names(self) -> list[str]:
        return sorted(self._workflows)

    def describe(self) -> list[dict[str, Any]]:
        return [
            workflow.to_dict()
            for workflow in sorted(self._workflows.values(), key=lambda w: w.name)
        ]

    def __iter__(self) -> Iterator[Workflow]:
        return iter(self._workflows.values())

    def __len__(self) -> int:
        return len(self._workflows)


def build_workflows(
    context: WorkflowContext,
    *,
    workflow_types: tuple[Type[Workflow], ...] = WORKFLOW_TYPES,
) -> WorkflowRegistry:
    registry = WorkflowRegistry()
    for workflow_type in workflow_types:
        registry.register(workflow_type(context))
    logger.info("registered %d workflows: %s", len(registry), ", ".join(registry.names()))
    return registry


__all__ = ["WORKFLOW_TYPES", "WorkflowRegistry", "build_workflows"]

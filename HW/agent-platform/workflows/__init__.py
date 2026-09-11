"""The workflows the platform can run."""

from .base import Workflow, WorkflowContext, WorkflowResult
from .bugfix import BugFixWorkflow
from .code_review import CodeReviewWorkflow
from .feature_development import FeatureDevelopmentWorkflow
from .log_analysis import LogAnalysisWorkflow
from .registry import WORKFLOW_TYPES, WorkflowRegistry, build_workflows
from .unit_test import GenerateUnitTestWorkflow

__all__ = [
    "BugFixWorkflow",
    "CodeReviewWorkflow",
    "FeatureDevelopmentWorkflow",
    "GenerateUnitTestWorkflow",
    "LogAnalysisWorkflow",
    "WORKFLOW_TYPES",
    "Workflow",
    "WorkflowContext",
    "WorkflowRegistry",
    "WorkflowResult",
    "build_workflows",
]

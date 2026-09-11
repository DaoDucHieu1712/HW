"""Event hook system: registration, dispatch and the built-in handlers."""

from .builtin import AuditTrail, RetryBudget, ToolGuard, UsageMeter, install_default_hooks
from .events import HookContext, HookEvent
from .manager import HookHandler, HookManager, HookProvider

__all__ = [
    "AuditTrail",
    "HookContext",
    "HookEvent",
    "HookHandler",
    "HookManager",
    "HookProvider",
    "RetryBudget",
    "ToolGuard",
    "UsageMeter",
    "install_default_hooks",
]

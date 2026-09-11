"""Plugin system: contract, manager and the plugins shipped with the platform."""

from .architecture_review import ArchitectureReviewPlugin
from .base import LLMReviewPlugin, Plugin, PluginContext, PluginResult
from .manager import BUILTIN_PLUGINS, PluginManager, build_plugin_manager
from .migration import MigrationPlugin
from .performance_review import PerformanceReviewPlugin
from .security_review import SecurityReviewPlugin

__all__ = [
    "BUILTIN_PLUGINS",
    "ArchitectureReviewPlugin",
    "LLMReviewPlugin",
    "MigrationPlugin",
    "PerformanceReviewPlugin",
    "Plugin",
    "PluginContext",
    "PluginManager",
    "PluginResult",
    "SecurityReviewPlugin",
    "build_plugin_manager",
]

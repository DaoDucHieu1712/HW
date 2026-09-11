"""Plugin discovery, lifecycle and stage execution.

The manager owns the lifecycle so a misbehaving plugin degrades the platform
instead of breaking it: registration failures disable one plugin, execution
failures are recorded as a failed result, and ``shutdown`` always runs.
"""

from __future__ import annotations

import importlib
import logging
from typing import Any, Iterable, Iterator, Sequence, Type

from graph.state import AgentState, ReviewComment

from .base import Plugin, PluginContext, PluginResult

logger = logging.getLogger(__name__)

#: Plugins shipped with the platform, keyed by the name used in configuration.
BUILTIN_PLUGINS: dict[str, str] = {
    "security_review": "plugins.security_review:SecurityReviewPlugin",
    "performance_review": "plugins.performance_review:PerformanceReviewPlugin",
    "architecture_review": "plugins.architecture_review:ArchitectureReviewPlugin",
    "migration": "plugins.migration:MigrationPlugin",
}


class PluginManager:
    """Loads plugins, holds them, and runs them by stage."""

    def __init__(self, context: PluginContext) -> None:
        self.context = context
        self._plugins: dict[str, Plugin] = {}
        self._disabled: dict[str, str] = {}

    # -- loading ---------------------------------------------------------

    def load(self, names: Iterable[str]) -> None:
        """Load and register the named plugins."""
        for name in names:
            target = BUILTIN_PLUGINS.get(name, name)
            try:
                plugin_type = _resolve(target)
            except (ImportError, AttributeError, ValueError) as exc:
                logger.error("could not load plugin %r: %s", name, exc)
                self._disabled[name] = str(exc)
                continue
            self.add(plugin_type())

    def add(self, plugin: Plugin) -> bool:
        """Register one plugin instance. Returns whether it is enabled."""
        try:
            plugin.register(self.context)
            problems = plugin.validate()
        except Exception as exc:  # noqa: BLE001 - a bad plugin must not stop startup
            logger.exception("plugin %s failed to register", plugin.name)
            self._disabled[plugin.name] = f"{type(exc).__name__}: {exc}"
            _safe_shutdown(plugin)
            return False

        if problems:
            logger.error("plugin %s disabled: %s", plugin.name, "; ".join(problems))
            self._disabled[plugin.name] = "; ".join(problems)
            _safe_shutdown(plugin)
            return False

        self._plugins[plugin.name] = plugin
        logger.info("plugin %s v%s registered (stages: %s)", plugin.name, plugin.version,
                    ", ".join(plugin.stages) or "-")
        return True

    # -- access ----------------------------------------------------------

    def get(self, name: str) -> Plugin:
        return self._plugins[name]

    def has(self, name: str) -> bool:
        return name in self._plugins

    def names(self) -> list[str]:
        return sorted(self._plugins)

    def for_stage(self, stage: str) -> list[Plugin]:
        return [plugin for plugin in self._plugins.values() if stage in plugin.stages]

    def describe(self) -> list[dict[str, Any]]:
        active = [plugin.to_dict() for plugin in self._plugins.values()]
        disabled = [
            {"name": name, "registered": False, "reason": reason}
            for name, reason in self._disabled.items()
        ]
        return sorted(active + disabled, key=lambda item: item["name"])

    # -- execution -------------------------------------------------------

    def run_stage(self, stage: str, state: AgentState) -> list[ReviewComment]:
        """Execute every plugin for ``stage`` and return their combined findings."""
        findings: list[ReviewComment] = []
        for result in self.execute_stage(stage, state):
            findings.extend(result.findings)
        return findings

    def execute_stage(self, stage: str, state: AgentState) -> list[PluginResult]:
        results: list[PluginResult] = []
        for plugin in self.for_stage(stage):
            try:
                result = plugin.execute(state)
            except Exception as exc:  # noqa: BLE001 - isolate plugin failure
                logger.exception("plugin %s raised during %s", plugin.name, stage)
                result = PluginResult(
                    plugin=plugin.name, ok=False, error=f"{type(exc).__name__}: {exc}"
                )
            results.append(result)
            logger.info(
                "plugin %s: %d finding(s)%s",
                plugin.name,
                len(result.findings),
                "" if result.ok else f" (failed: {result.error})",
            )
        return results

    # -- teardown --------------------------------------------------------

    def shutdown(self) -> None:
        for plugin in list(self._plugins.values()):
            _safe_shutdown(plugin)
        self._plugins.clear()

    def __iter__(self) -> Iterator[Plugin]:
        return iter(self._plugins.values())

    def __len__(self) -> int:
        return len(self._plugins)


def _resolve(target: str) -> Type[Plugin]:
    """Resolve ``package.module:ClassName`` to a plugin class."""
    module_name, separator, class_name = target.partition(":")
    if not separator:
        raise ValueError(f"plugin target must be 'module:Class', got {target!r}")
    module = importlib.import_module(module_name)
    plugin_type = getattr(module, class_name)
    if not issubclass(plugin_type, Plugin):
        raise TypeError(f"{target} is not a Plugin subclass")
    return plugin_type


def _safe_shutdown(plugin: Plugin) -> None:
    try:
        plugin.shutdown()
    except Exception:  # noqa: BLE001 - pragma: no cover
        logger.exception("plugin %s failed during shutdown", plugin.name)


def build_plugin_manager(context: PluginContext, names: Sequence[str]) -> PluginManager:
    manager = PluginManager(context)
    manager.load(names)
    return manager


__all__ = ["BUILTIN_PLUGINS", "PluginManager", "build_plugin_manager"]

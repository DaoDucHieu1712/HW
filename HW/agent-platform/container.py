"""Dependency injection container -- the composition root.

Every dependency in this platform is injected through a constructor, and this is
the one module that knows how the pieces fit together. Nothing else calls a
factory, reads configuration, or constructs a collaborator: agents receive an
``LLMClient``, tools receive a ``Workspace``, plugins receive a
``PluginContext``. That is what makes each layer testable in isolation.

Construction order matters and is enforced by the dependency arrows::

    settings -> hooks -> llm -> workspace -> tools -> mcp
             -> memory -> prompts -> skills -> agents -> plugins
             -> workflows -> commands
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any, Callable, TypeVar

from agents.registry import AgentRegistry, build_agents
from commands.registry import CommandRegistry, build_commands
from configs.settings import PlatformSettings, load_settings
from configs.telemetry import bootstrap_observability
from hooks.builtin import UsageMeter, install_default_hooks
from hooks.manager import HookManager
from llms.base import LLMClient
from llms.factory import build_llm
from mcps.manager import MCPManager, build_mcp_manager
from memory.manager import MemoryManager
from plugins.base import PluginContext
from plugins.manager import PluginManager, build_plugin_manager
from prompts.loader import PromptLibrary
from skills.registry import SkillRegistry
from tools import build_default_tools
from tools.registry import ToolRegistry
from tools.workspace import Workspace
from workflows.base import WorkflowContext
from workflows.registry import WorkflowRegistry, build_workflows

logger = logging.getLogger(__name__)

T = TypeVar("T")


@dataclass(slots=True)
class Container:
    """Holds the constructed object graph.

    Built by :func:`build_container`; treat the fields as read-only. Overrides
    exist so a test can swap one collaborator (an LLM, a tool registry) without
    reconstructing the rest.
    """

    settings: PlatformSettings
    hooks: HookManager
    usage: UsageMeter
    llm: LLMClient
    workspace: Workspace
    tools: ToolRegistry
    mcps: MCPManager
    memory: MemoryManager
    prompts: PromptLibrary
    skills: SkillRegistry
    agents: AgentRegistry
    plugins: PluginManager
    workflows: WorkflowRegistry
    commands: CommandRegistry
    _closers: list[Callable[[], None]] = field(default_factory=list)

    # -- lifecycle -------------------------------------------------------

    def shutdown(self) -> None:
        """Release everything, in reverse construction order."""
        for close in reversed(self._closers):
            try:
                close()
            except Exception:  # noqa: BLE001 - shutdown is best effort
                logger.exception("error during shutdown")
        self._closers.clear()

    def __enter__(self) -> "Container":
        return self

    def __exit__(self, *exc: object) -> None:
        self.shutdown()

    # -- introspection ---------------------------------------------------

    def describe(self) -> dict[str, Any]:
        """A snapshot of what is wired up -- what ``cli.py status`` prints."""
        return {
            "environment": self.settings.environment,
            "model": self.llm.model,
            "dry_run": self.settings.llm.dry_run,
            "workspace": str(self.workspace.root),
            "tools": self.tools.names(),
            "agents": self.agents.names(),
            "workflows": self.workflows.names(),
            "commands": self.commands.names(),
            "plugins": [plugin["name"] for plugin in self.plugins.describe()],
            "mcps": {
                server["name"]: server["enabled"] for server in self.mcps.describe()
            },
            "memory": self.memory.stats(),
            "skills": self.skills.names(),
            "usage": self.usage.snapshot(),
        }


def build_container(
    settings: PlatformSettings | None = None,
    *,
    config_path: str | None = None,
    llm: LLMClient | None = None,
    tools: ToolRegistry | None = None,
    memory: MemoryManager | None = None,
    register_mcp_tools: bool = True,
) -> Container:
    """Construct the whole platform.

    The keyword overrides are the seam tests use: pass an ``EchoLLM`` and a
    temporary workspace and the entire graph runs offline.
    """
    settings = settings or load_settings(config_path)
    bootstrap_observability(settings.observability)
    logger.info(
        "building platform (environment=%s, model=%s, dry_run=%s)",
        settings.environment,
        settings.llm.model,
        settings.llm.dry_run,
    )

    # 1. Hooks first: everything built after this can be observed and guarded.
    hooks = HookManager()
    usage = install_default_hooks(
        hooks,
        audit_path=settings.resolve(settings.observability.log_dir) / "audit.jsonl",
        allow_writes=settings.workspace.allow_writes,
    )

    # 2. The model.
    llm = llm or build_llm(settings.llm, hooks=hooks)

    # 3. Capability: workspace -> tools -> MCP tools.
    workspace = Workspace(settings.workspace)
    tools = tools or build_default_tools(settings, llm=llm, workspace=workspace, hooks=hooks)
    mcps = build_mcp_manager(settings, tools if register_mcp_tools else None)

    # 4. Knowledge: memory, prompts, skills.
    memory = memory or MemoryManager.from_settings(settings)
    prompts = PromptLibrary(settings.resolve(settings.prompts_dir))
    skills = SkillRegistry.from_directory(settings.resolve(settings.skills_dir))

    # 5. Agents, then plugins (plugins may add tools and hooks).
    agents = build_agents(
        settings,
        llm=llm,
        tools=tools,
        memory=memory,
        prompts=prompts,
        skills=skills,
        hooks=hooks,
    )
    plugins = build_plugin_manager(
        PluginContext(
            tools=tools, hooks=hooks, memory=memory, llm=llm, settings=settings
        ),
        settings.enabled_plugins,
    )

    # 6. Orchestration: workflows, then the commands that map onto them.
    workflows = build_workflows(
        WorkflowContext(agents=agents, plugins=plugins, graph_settings=settings.graph)
    )
    commands = build_commands(workflows)

    container = Container(
        settings=settings,
        hooks=hooks,
        usage=usage,
        llm=llm,
        workspace=workspace,
        tools=tools,
        mcps=mcps,
        memory=memory,
        prompts=prompts,
        skills=skills,
        agents=agents,
        plugins=plugins,
        workflows=workflows,
        commands=commands,
    )
    container._closers.extend([plugins.shutdown, mcps.shutdown, llm.close])
    logger.info(
        "platform ready: %d tools, %d agents, %d workflows, %d plugins",
        len(tools),
        len(agents),
        len(workflows),
        len(plugins),
    )
    return container


__all__ = ["Container", "build_container"]

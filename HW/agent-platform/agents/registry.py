"""Agent registry and factory.

This is the composition root for agents: the one place that knows how an agent
is built and how ``configs/agents.yaml`` overrides its defaults. Workflows and
the graph resolve agents by name, so adding one is a registration, not an edit
to every call site.
"""

from __future__ import annotations

import logging
from dataclasses import replace
from pathlib import Path
from typing import Any, Iterator, Type

import yaml

from configs.settings import CONFIG_DIR, PlatformSettings
from hooks.manager import HookManager
from llms.base import LLMClient
from memory.manager import MemoryManager
from prompts.loader import PromptLibrary
from skills.registry import SkillRegistry
from tools.registry import ToolRegistry

from .base import AgentConfig, BaseAgent
from .coding_agent import CodingAgent
from .documentation_agent import DocumentationAgent
from .jira_agent import JiraAgent
from .log_analysis_agent import LogAnalysisAgent
from .planner_agent import PlannerAgent
from .review_agent import ReviewAgent
from .supervisor_agent import SupervisorAgent
from .unittest_agent import UnitTestAgent

logger = logging.getLogger(__name__)

#: Every agent the platform ships with.
AGENT_TYPES: tuple[Type[BaseAgent], ...] = (
    SupervisorAgent,
    PlannerAgent,
    CodingAgent,
    UnitTestAgent,
    ReviewAgent,
    LogAnalysisAgent,
    JiraAgent,
    DocumentationAgent,
)


class AgentRegistry:
    """Name-addressed collection of constructed agents."""

    def __init__(self) -> None:
        self._agents: dict[str, BaseAgent] = {}

    def register(self, agent: BaseAgent, *, replace_existing: bool = False) -> BaseAgent:
        if agent.name in self._agents and not replace_existing:
            raise ValueError(f"agent {agent.name!r} is already registered")
        self._agents[agent.name] = agent
        return agent

    def get(self, name: str) -> BaseAgent:
        try:
            return self._agents[name]
        except KeyError as exc:
            raise KeyError(
                f"unknown agent {name!r}; registered: {', '.join(sorted(self._agents))}"
            ) from exc

    def has(self, name: str) -> bool:
        return name in self._agents

    def names(self) -> list[str]:
        return sorted(self._agents)

    def describe(self) -> list[dict[str, Any]]:
        return [
            {
                "name": agent.name,
                "description": agent.description,
                "tools": list(agent.available_tools()),
                "skills": list(agent.config.skills),
            }
            for agent in sorted(self._agents.values(), key=lambda a: a.name)
        ]

    def __iter__(self) -> Iterator[BaseAgent]:
        return iter(self._agents.values())

    def __len__(self) -> int:
        return len(self._agents)


def load_agent_overrides(path: str | Path | None = None) -> dict[str, dict[str, Any]]:
    """Read ``configs/agents.yaml`` -- absent or malformed means "no overrides"."""
    config_path = Path(path) if path else CONFIG_DIR / "agents.yaml"
    if not config_path.exists():
        return {}
    try:
        data = yaml.safe_load(config_path.read_text(encoding="utf-8")) or {}
    except yaml.YAMLError as exc:
        logger.error("ignoring invalid %s: %s", config_path, exc)
        return {}
    return dict(data.get("agents") or {})


def _apply_overrides(config: AgentConfig, overrides: dict[str, Any]) -> AgentConfig:
    """Overlay YAML values onto an agent's default config."""
    fields: dict[str, Any] = {}
    if "tools" in overrides:
        fields["tools"] = overrides["tools"]  # [] is meaningful: "no tools"
    if overrides.get("skills") is not None:
        fields["skills"] = list(overrides["skills"])
    for key in ("max_tokens", "max_tool_iterations"):
        if overrides.get(key) is not None:
            fields[key] = int(overrides[key])
    if overrides.get("effort"):
        fields["effort"] = str(overrides["effort"])
    if overrides.get("include_skill_examples") is not None:
        fields["include_skill_examples"] = bool(overrides["include_skill_examples"])
    return replace(config, **fields) if fields else config


def build_agents(
    settings: PlatformSettings,
    *,
    llm: LLMClient,
    tools: ToolRegistry,
    memory: MemoryManager,
    prompts: PromptLibrary,
    skills: SkillRegistry,
    hooks: HookManager | None = None,
    agent_types: tuple[Type[BaseAgent], ...] = AGENT_TYPES,
) -> AgentRegistry:
    """Construct every agent with its configuration applied."""
    overrides = load_agent_overrides()
    registry = AgentRegistry()

    for agent_type in agent_types:
        config = _apply_overrides(agent_type.default_config, overrides.get(agent_type.name, {}))
        # Drop tool names that are not registered, so a partially configured
        # environment degrades to fewer tools instead of failing to start.
        if config.tools:
            known = [name for name in config.tools if tools.has(name)]
            if len(known) != len(config.tools):
                missing = sorted(set(config.tools) - set(known))
                logger.warning(
                    "agent %s: dropping unregistered tool(s) %s",
                    agent_type.name,
                    ", ".join(missing),
                )
            config = replace(config, tools=known)

        registry.register(
            agent_type(
                llm=llm,
                tools=tools,
                memory=memory,
                prompts=prompts,
                skills=skills,
                hooks=hooks,
                config=config,
            )
        )

    logger.info("registered %d agents: %s", len(registry), ", ".join(registry.names()))
    return registry


__all__ = ["AGENT_TYPES", "AgentRegistry", "build_agents", "load_agent_overrides"]

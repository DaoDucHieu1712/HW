"""Base class for every agent.

An agent is the composition of five things the specification calls for:

* a **prompt template** (loaded from ``prompts/templates`` plus its skills)
* **tools registration** (names resolved against the shared registry)
* **memory access** (a :class:`memory.manager.MemoryManager`)
* **state access** (it reads :class:`graph.state.AgentState` and returns a patch)
* an **output schema** (JSON Schema, used for structured outputs and validation)

Subclasses supply those five and nothing else. The tool-calling loop, hook
emission, memory writes and error handling live here so every agent behaves the
same way under failure.
"""

from __future__ import annotations

import abc
import json
import logging
from dataclasses import dataclass, field
from typing import Any, Sequence

from graph.state import AgentState, Status
from hooks.events import HookEvent
from hooks.manager import HookManager
from llms.base import LLMClient, LLMMessage, LLMResponse, system_blocks
from memory.manager import MemoryManager
from prompts.loader import PromptLibrary
from skills.registry import SkillRegistry
from tools.registry import ToolRegistry

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class AgentConfig:
    """Per-agent knobs, overridable from ``configs/agents.yaml``."""

    #: Tool names this agent may call. ``None`` means every registered tool.
    tools: list[str] | None = None
    #: Skill names merged into the system prompt.
    skills: list[str] = field(default_factory=list)
    max_tokens: int = 8000
    #: ``low`` | ``medium`` | ``high`` | ``xhigh`` | ``max``.
    effort: str | None = None
    #: Cap on tool-call round trips within one turn.
    max_tool_iterations: int = 8
    #: Include worked examples from skills. Costs tokens; worth it for coding.
    include_skill_examples: bool = False


@dataclass(slots=True)
class AgentResult:
    """What an agent produced, before it is merged into the graph state."""

    agent: str
    ok: bool
    payload: dict[str, Any] = field(default_factory=dict)
    text: str = ""
    error: str | None = None
    tool_calls: list[str] = field(default_factory=list)
    usage: dict[str, int] = field(default_factory=dict)

    def to_history(self) -> dict[str, Any]:
        return {
            "agent": self.agent,
            "ok": self.ok,
            "tool_calls": self.tool_calls,
            "error": self.error,
            "summary": str(self.payload.get("summary", ""))[:500],
        }


class BaseAgent(abc.ABC):
    """Template method for a single agent turn."""

    #: Registry key and prompt template name.
    name: str = ""
    #: Shown in `--list` output and in the supervisor's routing prompt.
    description: str = ""
    #: Default configuration; ``configs/agents.yaml`` overrides it.
    default_config: AgentConfig = AgentConfig()
    #: JSON Schema the agent's answer must satisfy.
    output_schema: dict[str, Any] = {
        "type": "object",
        "properties": {"summary": {"type": "string"}},
        "required": ["summary"],
        "additionalProperties": True,
    }

    def __init__(
        self,
        *,
        llm: LLMClient,
        tools: ToolRegistry,
        memory: MemoryManager,
        prompts: PromptLibrary,
        skills: SkillRegistry,
        hooks: HookManager | None = None,
        config: AgentConfig | None = None,
    ) -> None:
        self.llm = llm
        self.tools = tools
        self.memory = memory
        self.prompts = prompts
        self.skills = skills
        self.hooks = hooks
        self.config = config or self.default_config

    # -- prompt assembly -------------------------------------------------

    def system_prompt(self, state: AgentState) -> list[dict[str, Any]]:
        """Build the system prompt as cacheable blocks.

        Stable content (role, skills) goes first and carries the cache
        breakpoint; the volatile task text is a user message, so it never
        invalidates the cached prefix.
        """
        role = (
            self.prompts.render(self.name, agent=self.name, description=self.description)
            if self.prompts.has(self.name)
            else self.description
        )
        parts = [role]
        skill_block = self.skills.render(
            self.config.skills, include_examples=self.config.include_skill_examples
        )
        if skill_block:
            parts.append(skill_block)
        parts.append(self._output_contract())
        return system_blocks(parts)

    def _output_contract(self) -> str:
        """Tell the model exactly what shape to answer in."""
        return (
            "## Response format\n"
            "Reply with a single JSON object and nothing else. It must match this schema:\n"
            f"```json\n{json.dumps(self.output_schema, indent=2)}\n```"
        )

    @abc.abstractmethod
    def build_prompt(self, state: AgentState) -> str:
        """The user message for this turn -- the task and its context."""

    def memory_query(self, state: AgentState) -> str:
        """What to retrieve from memory for this turn. Override to narrow it."""
        return f"{state.get('objective', '')} {state.get('task', '')}".strip()

    # -- result handling -------------------------------------------------

    @abc.abstractmethod
    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        """Translate the agent's payload into a state patch.

        Returning a *patch* rather than mutating state is what lets LangGraph
        merge concurrent branches and checkpoint between nodes.
        """

    # -- execution -------------------------------------------------------

    def run(self, state: AgentState) -> AgentResult:
        """One agent turn: prompt, tool loop, structured answer."""
        source = self.name
        if self.hooks is not None:
            before = self.hooks.emit(
                HookEvent.BEFORE_AGENT_EXECUTION,
                source,
                agent=self.name,
                status=state.get("status"),
                iteration=state.get("iteration"),
            )
            if before.cancelled:
                return AgentResult(
                    agent=self.name, ok=False, error=f"vetoed by hook: {before.cancel_reason}"
                )

        try:
            result = self._turn(state)
        except Exception as exc:  # noqa: BLE001 - an agent failure is a run event
            logger.exception("agent %s failed", self.name)
            if self.hooks is not None:
                self.hooks.emit_error(source, exc, agent=self.name)
            result = AgentResult(
                agent=self.name, ok=False, error=f"{type(exc).__name__}: {exc}"
            )

        if self.hooks is not None:
            self.hooks.emit(
                HookEvent.AFTER_AGENT_EXECUTION,
                source,
                agent=self.name,
                ok=result.ok,
                tool_calls=result.tool_calls,
            )
        return result

    def _turn(self, state: AgentState) -> AgentResult:
        system = self.system_prompt(state)
        prompt = self.build_prompt(state)

        memory_block = self.memory.context_block(self.memory_query(state))
        if memory_block:
            prompt = f"{memory_block}\n\n---\n\n{prompt}"

        messages: list[LLMMessage] = [LLMMessage.user(prompt)]
        schemas = self.tools.schemas(self.config.tools) if self.config.tools != [] else []
        called: list[str] = []
        usage: dict[str, int] = {}
        response: LLMResponse | None = None

        for iteration in range(self.config.max_tool_iterations):
            response = self.llm.complete(
                messages,
                system=system,
                tools=schemas or None,
                max_tokens=self.config.max_tokens,
                effort=self.config.effort,
            )
            _accumulate(usage, response.usage)

            if not response.wants_tools:
                break

            # Replay the assistant turn verbatim, then answer every tool call in
            # ONE user message -- splitting them teaches the model to stop
            # calling tools in parallel.
            messages.append(LLMMessage.assistant(response.raw_content))
            blocks: list[dict[str, Any]] = []
            for call in response.tool_calls:
                called.append(call.name)
                outcome = self.tools.execute(call.name, call.arguments, source=self.name)
                self.memory.observe(
                    f"{self.name}:{call.name}", outcome.to_content()[:2000], ok=outcome.ok
                )
                blocks.append(
                    {
                        "type": "tool_result",
                        "tool_use_id": call.id,
                        "content": outcome.to_content()[:20000],
                        **({"is_error": True} if not outcome.ok else {}),
                    }
                )
            messages.append(LLMMessage.user(blocks))
        else:
            logger.warning(
                "agent %s hit the tool-iteration cap (%d)",
                self.name,
                self.config.max_tool_iterations,
            )

        if response is None:  # pragma: no cover - the loop always runs once
            return AgentResult(agent=self.name, ok=False, error="no response from the model")

        payload = response.json()
        if not payload and response.text:
            payload = {"summary": response.text.strip()[:2000]}

        missing = [
            key for key in self.output_schema.get("required", []) if key not in payload
        ]
        if missing:
            logger.warning("agent %s omitted required field(s): %s", self.name, missing)

        self.memory.remember_turn("assistant", f"[{self.name}] {payload.get('summary', '')}")
        return AgentResult(
            agent=self.name,
            ok=not missing,
            payload=payload,
            text=response.text,
            error=None if not missing else f"missing field(s): {', '.join(missing)}",
            tool_calls=called,
            usage=usage,
        )

    # -- helpers ---------------------------------------------------------

    def available_tools(self) -> Sequence[str]:
        return [tool.name for tool in self.tools.subset(self.config.tools)]

    @staticmethod
    def advance(state: AgentState, status: Status) -> dict[str, Any]:
        """Standard patch fragment for a status transition."""
        return {"status": status.value}

    def __repr__(self) -> str:  # pragma: no cover - debug aid
        return f"<{type(self).__name__} name={self.name!r} tools={self.config.tools}>"


def _accumulate(target: dict[str, int], source: dict[str, int]) -> None:
    for key, value in (source or {}).items():
        target[key] = target.get(key, 0) + int(value or 0)


__all__ = ["AgentConfig", "AgentResult", "BaseAgent"]

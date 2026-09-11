"""Hook dispatch, tool registry policy, and memory behaviour."""

from __future__ import annotations

from pathlib import Path

import pytest

from hooks.builtin import RetryBudget, ToolGuard, UsageMeter
from hooks.events import HookContext, HookEvent
from hooks.manager import HookManager
from memory.base import MemoryRecord
from memory.long_term import LongTermMemory
from memory.manager import MemoryManager
from memory.short_term import ShortTermMemory
from memory.vector import VectorMemory
from tools.base import FunctionTool
from tools.registry import ToolRegistry


def _tool(name: str = "echo", read_only: bool = True) -> FunctionTool:
    return FunctionTool(
        name,
        "Echoes its input.",
        {"type": "object", "properties": {"value": {"type": "string"}}, "required": ["value"]},
        lambda value: value,
        read_only=read_only,
    )


class TestHookManager:
    def test_handlers_run_in_priority_order(self, hooks: HookManager) -> None:
        order: list[str] = []
        hooks.register(HookEvent.ON_ERROR, lambda ctx: order.append("late"), name="late", priority=50)
        hooks.register(HookEvent.ON_ERROR, lambda ctx: order.append("early"), name="early", priority=10)

        hooks.emit(HookEvent.ON_ERROR, "test")

        assert order == ["early", "late"]

    def test_a_failing_handler_does_not_stop_the_chain(self, hooks: HookManager) -> None:
        reached: list[str] = []

        def explode(context: HookContext) -> None:
            raise RuntimeError("handler bug")

        hooks.register(HookEvent.ON_ERROR, explode, name="explode", priority=1)
        hooks.register(HookEvent.ON_ERROR, lambda ctx: reached.append("yes"), name="after")

        hooks.emit(HookEvent.ON_ERROR, "test")

        assert reached == ["yes"]  # the bad handler was skipped, not fatal

    def test_cancelling_stops_later_handlers(self, hooks: HookManager) -> None:
        reached: list[str] = []
        hooks.register(
            HookEvent.BEFORE_TOOL_EXECUTION,
            lambda ctx: ctx.cancel("policy"),
            name="veto",
            priority=1,
        )
        hooks.register(
            HookEvent.BEFORE_TOOL_EXECUTION,
            lambda ctx: reached.append("ran"),
            name="after",
        )

        context = hooks.emit(HookEvent.BEFORE_TOOL_EXECUTION, "test")

        assert context.cancelled
        assert context.cancel_reason == "policy"
        assert reached == []

    def test_unregister_removes_a_handler(self, hooks: HookManager) -> None:
        remove = hooks.register(HookEvent.ON_ERROR, lambda ctx: None, name="temp")

        assert hooks.registered(HookEvent.ON_ERROR) == ["temp"]
        remove()
        assert hooks.registered(HookEvent.ON_ERROR) == []


class TestToolGuard:
    def test_blocks_a_denied_tool(self) -> None:
        context = HookContext(
            event=HookEvent.BEFORE_TOOL_EXECUTION, source="t", data={"tool": "write_file"}
        )

        ToolGuard(denied_tools=["write_file"])(context)

        assert context.cancelled

    def test_blocks_writes_in_read_only_mode(self) -> None:
        context = HookContext(
            event=HookEvent.BEFORE_TOOL_EXECUTION, source="t", data={"tool": "write_file"}
        )

        ToolGuard(allow_writes=False)(context)

        assert context.cancelled
        assert "read-only" in (context.cancel_reason or "")

    def test_leaves_a_read_tool_alone_in_read_only_mode(self) -> None:
        context = HookContext(
            event=HookEvent.BEFORE_TOOL_EXECUTION, source="t", data={"tool": "read_file"}
        )

        ToolGuard(allow_writes=False)(context)

        assert not context.cancelled

    def test_the_retry_budget_cancels_once_it_is_spent(self) -> None:
        budget = RetryBudget(max_errors=2)
        contexts = [
            HookContext(event=HookEvent.ON_ERROR, source="t") for _ in range(3)
        ]

        for context in contexts:
            budget(context)

        assert [c.cancelled for c in contexts] == [False, False, True]


class TestToolRegistry:
    def test_executes_a_registered_tool(self, hooks: HookManager) -> None:
        registry = ToolRegistry(hooks)
        registry.register(_tool())

        assert registry.execute("echo", {"value": "hi"}).data == "hi"

    def test_an_unknown_tool_is_a_failure_not_an_exception(self) -> None:
        result = ToolRegistry().execute("nope", {})

        assert not result.ok
        assert "unknown tool" in (result.error or "")

    def test_a_before_hook_can_veto_execution(self, hooks: HookManager) -> None:
        registry = ToolRegistry(hooks)
        registry.register(_tool("write_thing", read_only=False))
        hooks.register(
            HookEvent.BEFORE_TOOL_EXECUTION, ToolGuard(allow_writes=False), name="guard"
        )
        hooks.register(
            HookEvent.BEFORE_TOOL_EXECUTION,
            lambda ctx: ctx.cancel("denied") if ctx.data.get("tool") == "write_thing" else None,
            name="explicit",
        )

        result = registry.execute("write_thing", {"value": "x"})

        assert not result.ok
        assert result.metadata.get("vetoed") is True

    def test_a_before_hook_can_rewrite_the_arguments(self, hooks: HookManager) -> None:
        registry = ToolRegistry(hooks)
        registry.register(_tool())
        hooks.register(
            HookEvent.BEFORE_TOOL_EXECUTION,
            lambda ctx: ctx.data.__setitem__("arguments", {"value": "rewritten"}),
            name="rewrite",
        )

        assert registry.execute("echo", {"value": "original"}).data == "rewritten"

    def test_subset_distinguishes_no_tools_from_every_tool(self) -> None:
        registry = ToolRegistry()
        registry.register(_tool("a"))
        registry.register(_tool("b"))

        assert len(registry.subset(None)) == 2  # None means "all"
        assert registry.subset([]) == []  # [] means "none"

    def test_registering_a_duplicate_name_is_refused(self) -> None:
        registry = ToolRegistry()
        registry.register(_tool())

        with pytest.raises(ValueError, match="already registered"):
            registry.register(_tool())

    def test_the_usage_meter_counts_what_ran(self, hooks: HookManager) -> None:
        meter = UsageMeter()
        hooks.register(HookEvent.AFTER_TOOL_EXECUTION, meter.on_after_tool, name="count")
        registry = ToolRegistry(hooks)
        registry.register(_tool())

        registry.execute("echo", {"value": "one"})
        registry.execute("echo", {"value": "two"})

        assert meter.snapshot()["tool_calls"] == 2


class TestMemory:
    def test_short_term_memory_drops_the_oldest_entries(self) -> None:
        memory = ShortTermMemory(max_items=3)

        for index in range(5):
            memory.remember_turn("user", f"message {index}")

        assert len(memory) == 3
        assert "message 4" in memory.transcript()
        assert "message 0" not in memory.transcript()

    def test_long_term_memory_survives_a_reload(self, tmp_path: Path) -> None:
        path = tmp_path / "lt.json"
        LongTermMemory(path).remember("Orders are cancelled on the aggregate", kind="fact")

        reloaded = LongTermMemory(path)

        assert len(reloaded) == 1
        assert "aggregate" in reloaded.all()[0].content

    def test_long_term_memory_does_not_duplicate_the_same_fact(self, tmp_path: Path) -> None:
        memory = LongTermMemory(tmp_path / "lt.json")
        memory.remember("same fact")
        memory.remember("same fact")

        assert len(memory) == 1

    def test_a_wrong_memory_can_be_deleted(self, tmp_path: Path) -> None:
        memory = LongTermMemory(tmp_path / "lt.json")
        record = memory.remember("this turned out to be wrong")

        assert memory.forget(record.id)
        assert len(memory) == 0

    def test_vector_memory_falls_back_without_chroma(self, tmp_path: Path) -> None:
        memory = VectorMemory(path=str(tmp_path / "v"), backend="memory")
        memory.add(MemoryRecord(content="the outbox flushes inside the transaction"))

        found = memory.search("outbox transaction", limit=3)

        assert memory.backend == "memory"
        assert found and "outbox" in found[0].content

    def test_the_context_block_is_empty_when_nothing_is_known(
        self, memory: MemoryManager
    ) -> None:
        assert memory.context_block("anything") == ""

    def test_the_context_block_includes_what_was_learned(
        self, memory: MemoryManager
    ) -> None:
        memory.learn("Cancellation is refused after shipping", kind="rule")

        block = memory.context_block("cancellation shipping")

        assert "Known project context" in block
        assert "refused after shipping" in block

    def test_resetting_a_run_keeps_durable_knowledge(self, memory: MemoryManager) -> None:
        memory.learn("durable fact")
        memory.observe("tool", "transient observation")

        memory.reset_run()

        assert memory.stats()["short_term"] == 0
        assert memory.stats()["long_term"] == 1

"""Agent behaviour: prompt assembly, the tool loop, and state patches."""

from __future__ import annotations

import json

from agents.base import AgentConfig, AgentResult
from agents.coding_agent import CodingAgent
from agents.planner_agent import PlannerAgent
from agents.review_agent import ReviewAgent
from agents.unittest_agent import UnitTestAgent
from graph.state import Status, initial_state
from llms.fake import EchoLLM, json_response, tool_call_response
from prompts.loader import PromptLibrary
from skills.registry import Skill, SkillRegistry
from tools.base import FunctionTool
from tools.registry import ToolRegistry


def _build(agent_type, llm, memory, *, tools: ToolRegistry | None = None, config=None):
    registry = tools or ToolRegistry()
    skills = SkillRegistry()
    skills.register(
        Skill(name="cqrs", description="d", prompt="Slice by use case.", applies_to=())
    )
    return agent_type(
        llm=llm,
        tools=registry,
        memory=memory,
        prompts=PromptLibrary("prompts/templates"),
        skills=skills,
        config=config or AgentConfig(tools=[], skills=["cqrs"], max_tool_iterations=3),
    )


class TestPromptAssembly:
    def test_the_system_prompt_carries_role_skills_and_schema(self, llm, memory) -> None:
        agent = _build(PlannerAgent, llm, memory)

        blocks = agent.system_prompt(initial_state("fix the thing"))
        text = "\n".join(block["text"] for block in blocks)

        assert "Planner" in text
        assert "Slice by use case." in text
        assert '"verification"' in text  # the output contract is in the prompt

    def test_the_cache_breakpoint_sits_on_the_last_stable_block(self, llm, memory) -> None:
        blocks = _build(PlannerAgent, llm, memory).system_prompt(initial_state("t"))

        assert blocks[-1]["cache_control"] == {"type": "ephemeral"}
        assert all("cache_control" not in block for block in blocks[:-1])

    def test_the_task_goes_in_a_user_message_not_the_system_prompt(
        self, llm, memory
    ) -> None:
        # Volatile text after the cache breakpoint would invalidate the prefix.
        agent = _build(PlannerAgent, llm, memory)
        state = initial_state("fix the flaky import")

        agent.run(state)

        assert "flaky import" in json.dumps(llm.calls[0]["messages"])
        assert "flaky import" not in json.dumps(llm.calls[0]["system"])

    def test_a_replan_tells_the_agent_what_failed(self, llm, memory) -> None:
        agent = _build(PlannerAgent, llm, memory)
        state = initial_state("t")
        state["iteration"] = 2
        state["test_results"] = [{"kind": "unit", "success": False, "output": "assert failed"}]

        prompt = agent.build_prompt(state)

        assert "revision 2" in prompt
        assert "assert failed" in prompt


class TestToolLoop:
    def test_a_tool_call_is_executed_and_answered(self, memory) -> None:
        calls: list[str] = []
        registry = ToolRegistry()
        registry.register(
            FunctionTool(
                "search_code",
                "Search.",
                {"type": "object", "properties": {"pattern": {"type": "string"}}, "required": ["pattern"]},
                lambda pattern: calls.append(pattern) or "match at Order.cs:12",
            )
        )
        llm = EchoLLM(
            [
                tool_call_response("search_code", {"pattern": "Cancel"}),
                json_response({"summary": "found it", "plan": ["step"], "verification": "tests"}),
            ]
        )
        agent = _build(
            PlannerAgent,
            llm,
            memory,
            tools=registry,
            config=AgentConfig(tools=["search_code"], skills=[]),
        )

        result = agent.run(initial_state("find Cancel"))

        assert calls == ["Cancel"]
        assert result.tool_calls == ["search_code"]
        assert result.payload["summary"] == "found it"

    def test_tool_results_are_returned_in_one_user_message(self, memory) -> None:
        # Splitting them teaches the model to stop calling tools in parallel.
        registry = ToolRegistry()
        registry.register(
            FunctionTool("a", "A.", {"type": "object", "properties": {}}, lambda: "ra")
        )
        from llms.base import LLMResponse, ToolCall

        parallel = LLMResponse(
            text="",
            tool_calls=[ToolCall("1", "a", {}), ToolCall("2", "a", {})],
            stop_reason="tool_use",
            raw_content=[{"type": "tool_use", "id": "1", "name": "a", "input": {}}],
        )
        llm = EchoLLM([parallel, json_response({"summary": "done"})])
        agent = _build(
            PlannerAgent, llm, memory, tools=registry, config=AgentConfig(tools=["a"], skills=[])
        )

        agent.run(initial_state("t"))

        second_request = llm.calls[1]["messages"]
        tool_result_messages = [
            message
            for message in second_request
            if isinstance(message["content"], list)
            and any(block.get("type") == "tool_result" for block in message["content"])
        ]
        assert len(tool_result_messages) == 1
        assert len(tool_result_messages[0]["content"]) == 2

    def test_a_failed_tool_is_reported_back_as_an_error_block(self, memory) -> None:
        registry = ToolRegistry()
        registry.register(
            FunctionTool(
                "boom",
                "Fails.",
                {"type": "object", "properties": {}},
                lambda: (_ for _ in ()).throw(RuntimeError("no such file")),
            )
        )
        llm = EchoLLM(
            [tool_call_response("boom", {}), json_response({"summary": "handled"})]
        )
        agent = _build(
            PlannerAgent, llm, memory, tools=registry, config=AgentConfig(tools=["boom"], skills=[])
        )

        agent.run(initial_state("t"))

        blocks = llm.calls[1]["messages"][-1]["content"]
        assert blocks[0]["is_error"] is True
        assert "no such file" in blocks[0]["content"]

    def test_the_loop_stops_at_the_iteration_cap(self, memory) -> None:
        registry = ToolRegistry()
        registry.register(
            FunctionTool("a", "A.", {"type": "object", "properties": {}}, lambda: "r")
        )
        # Always asks for another tool call: the cap is the only thing that stops it.
        llm = EchoLLM([tool_call_response("a", {}) for _ in range(10)])
        agent = _build(
            PlannerAgent,
            llm,
            memory,
            tools=registry,
            config=AgentConfig(tools=["a"], skills=[], max_tool_iterations=3),
        )

        agent.run(initial_state("t"))

        assert len(llm.calls) == 3


class TestStatePatches:
    def test_the_planner_records_the_plan_and_the_root_cause(self, llm, memory) -> None:
        agent = _build(PlannerAgent, llm, memory)
        result = AgentResult(
            agent="planner",
            ok=True,
            payload={
                "summary": "s",
                "plan": ["one", "two"],
                "verification": "dotnet test",
                "root_cause": "the handler swallows the cancellation",
            },
        )

        patch = agent.apply(initial_state("t"), result)

        assert patch["plan"] == ["one", "two"]
        assert patch["status"] == Status.IN_PROGRESS.value
        assert "swallows" in patch["artifacts"]["root_cause"]

    def test_a_root_cause_is_remembered_as_durable_knowledge(self, llm, memory) -> None:
        agent = _build(PlannerAgent, llm, memory)

        agent.apply(
            initial_state("BUG-1"),
            AgentResult(
                agent="planner",
                ok=True,
                payload={
                    "summary": "s",
                    "plan": ["p"],
                    "verification": "v",
                    "root_cause": "double dispatch of the outbox message",
                },
            ),
        )

        assert memory.stats()["long_term"] == 1

    def test_a_failed_build_blocks_progress_to_testing(self, llm, memory) -> None:
        agent = _build(CodingAgent, llm, memory)
        result = AgentResult(
            agent="coding",
            ok=True,
            payload={
                "summary": "s",
                "changes": [{"path": "Order.cs", "action": "modified"}],
                "build_succeeded": False,
                "build_output": "CS0103",
            },
        )

        patch = agent.apply(initial_state("t"), result)

        assert patch["status"] == Status.NEEDS_REVISION.value
        assert patch["test_results"][0] == {
            "kind": "build",
            "success": False,
            "output": "CS0103",
            "command": "build_solution",
        }

    def test_the_test_agent_flags_a_regression_test_it_never_saw_fail(
        self, llm, memory
    ) -> None:
        agent = _build(UnitTestAgent, llm, memory)
        state = initial_state("t")
        state["artifacts"] = {"root_cause": "off by one"}

        patch = agent.apply(
            state,
            AgentResult(
                agent="unittest",
                ok=True,
                payload={
                    "summary": "s",
                    "tests_passed": True,
                    "would_have_caught_the_bug": False,
                },
            ),
        )

        assert "test_warning" in patch["artifacts"]

    def test_the_reviewer_cannot_approve_unverified_code(self, llm, memory) -> None:
        agent = _build(ReviewAgent, llm, memory)
        # Approved by the model, but nothing was ever built or run.
        patch = agent.apply(
            initial_state("t"),
            AgentResult(
                agent="review",
                ok=True,
                payload={"summary": "looks fine", "approved": True, "comments": []},
            ),
        )

        assert patch["status"] == Status.NEEDS_REVISION.value
        assert "verification has not passed" in patch["errors"][0]

    def test_the_reviewer_approves_verified_code(self, llm, memory) -> None:
        agent = _build(ReviewAgent, llm, memory)
        state = initial_state("t")
        state["test_results"] = [
            {"kind": "build", "success": True},
            {"kind": "unit", "success": True},
        ]

        patch = agent.apply(
            state,
            AgentResult(
                agent="review",
                ok=True,
                payload={"summary": "sound", "approved": True, "comments": []},
            ),
        )

        assert patch["status"] == Status.SUCCESS.value

    def test_a_read_only_review_does_not_wait_for_build_evidence(
        self, llm, memory
    ) -> None:
        # A review workflow never builds, so requiring green evidence would
        # make its verdict unreachable.
        agent = _build(ReviewAgent, llm, memory)
        state = initial_state("t")
        state["context"] = {"verification_required": False}

        patch = agent.apply(
            state,
            AgentResult(
                agent="review",
                ok=True,
                payload={"summary": "sound", "approved": True, "comments": []},
            ),
        )

        assert patch["status"] == Status.SUCCESS.value

    def test_a_blocking_finding_overrides_the_models_own_approval(
        self, llm, memory
    ) -> None:
        agent = _build(ReviewAgent, llm, memory)
        state = initial_state("t")
        state["test_results"] = [
            {"kind": "build", "success": True},
            {"kind": "unit", "success": True},
        ]

        patch = agent.apply(
            state,
            AgentResult(
                agent="review",
                ok=True,
                payload={
                    "summary": "mostly fine",
                    "approved": True,
                    "comments": [
                        {"severity": "blocker", "category": "security", "message": "injection"}
                    ],
                },
            ),
        )

        assert patch["status"] == Status.NEEDS_REVISION.value


class TestDeliveryStep:
    def test_documentation_settles_a_finished_run(self, llm, memory) -> None:
        from agents.documentation_agent import DocumentationAgent

        agent = _build(DocumentationAgent, llm, memory)
        state = initial_state("t")
        state["status"] = Status.REVIEWING.value

        patch = agent.apply(
            state,
            AgentResult(
                agent="documentation",
                ok=True,
                payload={"summary": "s", "pr_title": "Fix cancellation", "pr_body": "body"},
            ),
        )

        assert patch["status"] == Status.SUCCESS.value
        assert patch["artifacts"]["pr_title"] == "Fix cancellation"

    def test_documentation_does_not_paper_over_an_upstream_failure(
        self, llm, memory
    ) -> None:
        from agents.documentation_agent import DocumentationAgent

        agent = _build(DocumentationAgent, llm, memory)
        state = initial_state("t")
        state["status"] = Status.FAILED.value

        patch = agent.apply(
            state,
            AgentResult(
                agent="documentation",
                ok=True,
                payload={"summary": "s", "pr_title": "t", "pr_body": "b"},
            ),
        )

        assert patch["status"] == Status.FAILED.value


class TestOutputValidation:
    def test_a_missing_required_field_marks_the_result_as_not_ok(self, memory) -> None:
        llm = EchoLLM([json_response({"summary": "no plan here"})])
        agent = _build(PlannerAgent, llm, memory)

        result = agent.run(initial_state("t"))

        assert not result.ok
        assert "plan" in (result.error or "")

    def test_prose_around_the_json_is_still_parsed(self, memory) -> None:
        llm = EchoLLM(
            ['Here is my answer:\n```json\n{"summary": "ok", "plan": ["a"], "verification": "v"}\n```\nHope that helps.']
        )
        agent = _build(PlannerAgent, llm, memory)

        result = agent.run(initial_state("t"))

        assert result.ok
        assert result.payload["plan"] == ["a"]

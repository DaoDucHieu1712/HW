"""End-to-end tests: commands, workflows, plugins and MCP registration.

These run the real object graph -- container, agents, tools, plugins -- with
only the model replaced.
"""

from __future__ import annotations

import pytest

from commands.base import CommandError, parse_command_line
from graph.state import Status, initial_state
from mcps.base import MCPUnavailable, NullTransport
from mcps.manager import MCPManager
from plugins.architecture_review import layer_of
from plugins.migration import scan_migration
from plugins.security_review import SecurityReviewPlugin
from configs.settings import MCPServerSettings
from mcps.servers import FilesystemMCPServer


class TestCommandParsing:
    def test_parses_a_slash_command_with_options(self) -> None:
        parsed = parse_command_line("/fixbug BUG-123 --max-iterations 5 --notes 'be careful'")

        assert parsed.name == "fixbug"
        assert parsed.positional == ["BUG-123"]
        assert parsed.int_option("max-iterations") == 5
        assert parsed.option("notes") == "be careful"

    def test_the_leading_slash_is_optional(self) -> None:
        assert parse_command_line("review src/a.cs").name == "review"

    def test_supports_inline_option_syntax(self) -> None:
        parsed = parse_command_line("/unittest a.cs --framework=nunit")

        assert parsed.option("framework") == "nunit"

    def test_a_bare_flag_parses_as_a_flag(self) -> None:
        assert parse_command_line("/review --json").flag("json") is True

    def test_an_empty_line_is_an_error(self) -> None:
        with pytest.raises(CommandError):
            parse_command_line("   ")

    def test_unbalanced_quotes_are_reported_clearly(self) -> None:
        with pytest.raises(CommandError, match="could not parse"):
            parse_command_line('/newfeature "unclosed')


class TestCommandRegistry:
    def test_every_builtin_command_maps_to_a_registered_workflow(self, container) -> None:
        for entry in container.commands.describe():
            assert container.workflows.has(entry["workflow"])

    def test_aliases_resolve_to_the_same_command(self, container) -> None:
        assert container.commands.get("bugfix") is container.commands.get("fixbug")

    def test_an_unknown_command_lists_the_known_ones(self, container) -> None:
        with pytest.raises(CommandError, match="unknown command"):
            container.commands.get("nonsense")

    def test_a_command_missing_its_argument_is_refused(self, container) -> None:
        with pytest.raises(CommandError, match="issue key is required"):
            container.commands.dispatch("/fixbug")


class TestWorkflows:
    def test_the_bugfix_graph_covers_the_specified_steps(self, container) -> None:
        spec = container.workflows.get("bugfix").build_spec()

        assert spec.entry == "jira"
        for node in ("jira", "triage", "planner", "coding", "unittest", "review", "deliver"):
            assert node in spec.nodes
        spec.validate()  # raises if any edge points at a node that does not exist

    def test_every_registered_workflow_builds_a_valid_graph(self, container) -> None:
        for workflow in container.workflows:
            workflow.build_spec().validate()

    def test_a_workflow_refuses_to_run_without_its_required_arguments(
        self, container
    ) -> None:
        with pytest.raises(ValueError, match="requires"):
            container.workflows.get("bugfix").run()

    def test_the_bugfix_workflow_runs_end_to_end_offline(self, container) -> None:
        result = container.commands.dispatch("/fixbug DEMO-1")

        # The stub model reports success at every step, so the happy path runs
        # through to delivery. This proves the wiring, not the code quality.
        assert result.status == Status.SUCCESS.value
        agents_run = [entry["agent"] for entry in result.state["history"]]
        assert agents_run == [
            "jira",
            "log_analysis",
            "planner",
            "coding",
            "unittest",
            "review",
            "documentation",
        ]

    def test_the_review_workflow_is_read_only_and_single_pass(self, container) -> None:
        spec = container.workflows.get("review").build_spec()

        assert "coding" not in spec.nodes
        assert "iterate" not in spec.nodes

    def test_read_only_workflows_declare_that_they_produce_no_evidence(
        self, container
    ) -> None:
        for name in ("review", "analyzelog"):
            workflow = container.workflows.get(name)
            state = (
                workflow.seed_state(paths=["a.cs"])
                if name == "review"
                else workflow.seed_state(correlation_id="abc123")
            )
            assert state["context"]["verification_required"] is False

    def test_the_log_analysis_workflow_needs_something_to_anchor_on(
        self, container
    ) -> None:
        with pytest.raises(ValueError, match="correlation_id or query"):
            container.workflows.get("analyzelog").run()

    def test_seeding_the_review_workflow_records_the_paths_under_review(
        self, container
    ) -> None:
        state = container.workflows.get("review").seed_state(paths=["a.cs", "b.cs"])

        assert [change["path"] for change in state["code_changes"]] == ["a.cs", "b.cs"]

    def test_a_workflow_run_is_reported_even_when_it_fails(self, container) -> None:
        workflow = container.workflows.get("bugfix")

        class Boom:
            def invoke(self, state, config=None):
                raise RuntimeError("graph exploded")

        workflow._graph = Boom()

        result = workflow.run(issue_key="X-1")

        assert result.status == Status.FAILED.value
        assert "graph exploded" in (result.error or "")


class TestPlugins:
    def test_the_security_scan_finds_a_hardcoded_credential(self, container) -> None:
        plugin = SecurityReviewPlugin()
        plugin.register(
            __import__("plugins.base", fromlist=["PluginContext"]).PluginContext(
                tools=container.tools,
                hooks=container.hooks,
                memory=container.memory,
                llm=container.llm,
                settings=container.settings,
            )
        )
        state = initial_state("review")
        state["code_changes"] = [{"path": "src/OrderService.cs", "action": "modified"}]

        findings = plugin._scan(state)

        assert any("Hardcoded credential" in finding["message"] for finding in findings)
        assert all(finding["severity"] == "blocker" for finding in findings)

    def test_the_architecture_plugin_infers_the_layer_from_the_path(self) -> None:
        assert layer_of("HW.Domain/Orders/Order.cs") == "domain"
        assert layer_of("src/Application/Handlers/X.cs") == "application"
        assert layer_of("README.md") is None

    def test_the_migration_scan_flags_destructive_statements(self) -> None:
        findings = scan_migration(
            "ALTER TABLE Orders ADD Notes NVARCHAR(200);\n"
            "DROP TABLE OrderArchive;\n"
            "-- DROP TABLE CommentedOut;\n",
            "Migrations/001.sql",
        )

        assert len(findings) == 1
        assert findings[0]["line"] == 2
        assert findings[0]["severity"] == "blocker"

    def test_a_plugin_that_fails_validation_is_disabled_not_half_wired(
        self, container
    ) -> None:
        from plugins.base import Plugin, PluginContext, PluginResult

        class Broken(Plugin):
            name = "broken"
            stages = ("review",)

            def register(self, context: PluginContext) -> None:
                self._bind(context)

            def validate(self) -> list[str]:
                return ["missing configuration"]

            def execute(self, state) -> PluginResult:  # pragma: no cover
                raise AssertionError("must never run")

        enabled = container.plugins.add(Broken())

        assert enabled is False
        assert not container.plugins.has("broken")

    def test_a_plugin_that_raises_does_not_break_the_stage(self, container) -> None:
        from plugins.base import Plugin, PluginContext, PluginResult

        class Exploding(Plugin):
            name = "exploding"
            stages = ("review",)

            def register(self, context: PluginContext) -> None:
                self._bind(context)

            def execute(self, state) -> PluginResult:
                raise RuntimeError("kaboom")

        container.plugins.add(Exploding())

        results = container.plugins.execute_stage("review", initial_state("t"))

        assert any(result.plugin == "exploding" and not result.ok for result in results)


class TestMCP:
    def test_a_disabled_server_is_described_but_registers_no_tools(self) -> None:
        server = FilesystemMCPServer(MCPServerSettings(name="filesystem", enabled=False))
        manager = MCPManager()
        manager.add(server)

        from tools.registry import ToolRegistry

        registry = ToolRegistry()

        assert manager.register_tools(registry) == 0
        assert server.declared_tools()  # still documented

    def test_a_disabled_server_reports_unhealthy_with_the_reason(self) -> None:
        server = FilesystemMCPServer(MCPServerSettings(name="filesystem", enabled=False))

        status = server.health_check()

        assert status.healthy is False
        assert "disabled" in status.detail

    def test_missing_credentials_are_reported_before_a_connection_is_attempted(
        self, monkeypatch
    ) -> None:
        monkeypatch.delenv("A_TOKEN_THAT_IS_NOT_SET", raising=False)
        server = FilesystemMCPServer(
            MCPServerSettings(
                name="filesystem",
                enabled=True,
                env={"TOKEN": "${A_TOKEN_THAT_IS_NOT_SET}"},
            )
        )

        status = server.health_check()

        assert status.healthy is False
        assert "A_TOKEN_THAT_IS_NOT_SET" in status.detail

    def test_calling_an_unavailable_server_fails_loudly(self) -> None:
        server = FilesystemMCPServer(
            MCPServerSettings(name="filesystem", enabled=True),
            transport=NullTransport("not configured"),
        )

        with pytest.raises(MCPUnavailable):
            server.call_tool("read_file", {"path": "x"})

    def test_an_mcp_tool_is_indistinguishable_from_a_native_one(self) -> None:
        server = FilesystemMCPServer(MCPServerSettings(name="filesystem", enabled=False))

        tool = server.as_tools()[0]

        assert tool.name.startswith("filesystem__")
        schema = tool.to_anthropic_schema()
        assert schema["name"] == tool.name
        assert "input_schema" in schema


class TestContainer:
    def test_everything_the_specification_asks_for_is_wired(self, container) -> None:
        summary = container.describe()

        assert len(summary["agents"]) == 8
        assert len(summary["workflows"]) == 5
        assert len(summary["commands"]) == 5
        assert set(summary["tools"]) >= {
            "search_code",
            "read_file",
            "write_file",
            "analyze_logs",
            "query_jira",
            "query_database",
            "build_solution",
            "run_tests",
            "create_pull_request",
            "generate_unit_test",
        }

    def test_agents_only_receive_tools_that_exist(self, container) -> None:
        for agent in container.agents:
            for name in agent.available_tools():
                assert container.tools.has(name)

    def test_shutdown_is_idempotent(self, container) -> None:
        container.shutdown()
        container.shutdown()  # must not raise

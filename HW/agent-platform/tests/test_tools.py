"""Tool tests -- each tool is exercised on its own, against a real directory."""

from __future__ import annotations

from pathlib import Path

import pytest

from tools.base import FunctionTool, ToolResult
from tools.code_tools import parse_build_errors, parse_test_summary
from tools.db_tools import QueryDatabaseTool, assert_read_only
from tools.filesystem_tools import ReadFileTool, SearchCodeTool, WriteFileTool
from tools.base import ToolError
from tools.workspace import CommandOutcome, Workspace, WorkspaceError


class TestWorkspaceSandbox:
    """The path guard is the platform's deterministic boundary."""

    def test_resolves_a_path_inside_the_root(self, workspace: Workspace) -> None:
        resolved = workspace.resolve("src/Order.cs", must_exist=True)

        assert resolved.name == "Order.cs"
        assert workspace.relative(resolved) == "src/Order.cs"

    @pytest.mark.parametrize(
        "escape",
        ["../outside.txt", "../../etc/passwd", "src/../../escape.cs"],
    )
    def test_refuses_paths_that_escape_the_root(
        self, workspace: Workspace, escape: str
    ) -> None:
        with pytest.raises(WorkspaceError, match="escapes the workspace root"):
            workspace.resolve(escape)

    def test_refuses_a_denied_glob(self, workspace: Workspace) -> None:
        with pytest.raises(WorkspaceError, match="denied by policy"):
            workspace.resolve("secrets/keys.txt")

    def test_refuses_to_write_in_read_only_mode(self, workspace: Workspace) -> None:
        workspace.settings.allow_writes = False

        with pytest.raises(WorkspaceError, match="read-only"):
            workspace.write_text("src/New.cs", "content")

    def test_refuses_a_file_over_the_size_limit(self, workspace: Workspace) -> None:
        workspace.settings.max_file_bytes = 10

        with pytest.raises(WorkspaceError, match="over the"):
            workspace.read_text("src/Order.cs")


class TestReadFileTool:
    def test_returns_numbered_lines(self, workspace: Workspace) -> None:
        result = ReadFileTool(workspace).run(path="src/Order.cs")

        assert result.ok
        assert "1| namespace HW.Domain;" in result.data
        assert result.metadata["total_lines"] == 7

    def test_returns_only_the_requested_range(self, workspace: Workspace) -> None:
        result = ReadFileTool(workspace).run(path="src/Order.cs", start_line=3, end_line=4)

        assert result.metadata["returned_lines"] == 2
        assert result.data.strip().startswith("3|")

    def test_reports_a_missing_file_as_a_failure_not_an_exception(
        self, workspace: Workspace
    ) -> None:
        result = ReadFileTool(workspace).run(path="src/Nope.cs")

        assert not result.ok
        assert "does not exist" in (result.error or "")

    def test_requires_the_path_argument(self, workspace: Workspace) -> None:
        result = ReadFileTool(workspace).run()

        assert not result.ok
        assert "missing required argument" in (result.error or "")


class TestWriteFileTool:
    def test_creates_a_file_and_reports_it_as_created(self, workspace: Workspace) -> None:
        result = WriteFileTool(workspace).run(path="src/New.cs", content="class New { }")

        assert result.ok
        assert result.metadata["created"] is True
        assert (workspace.root / "src" / "New.cs").read_text(encoding="utf-8") == "class New { }"

    def test_overwriting_reports_it_as_an_update(self, workspace: Workspace) -> None:
        tool = WriteFileTool(workspace)
        tool.run(path="src/New.cs", content="one")

        result = tool.run(path="src/New.cs", content="two")

        assert result.metadata["created"] is False

    def test_is_not_read_only(self) -> None:
        assert WriteFileTool.read_only is False


class TestSearchCodeTool:
    def test_finds_matching_lines_with_their_location(self, workspace: Workspace) -> None:
        result = SearchCodeTool(workspace).run(pattern=r"class\s+Order\b")

        assert result.ok
        assert any(match["path"] == "src/Order.cs" for match in result.data)
        assert all(match["line"] > 0 for match in result.data)

    def test_reports_an_invalid_regex_clearly(self, workspace: Workspace) -> None:
        result = SearchCodeTool(workspace).run(pattern="([unclosed")

        assert not result.ok
        assert "invalid regular expression" in (result.error or "")

    def test_marks_the_result_as_truncated_at_the_limit(self, workspace: Workspace) -> None:
        result = SearchCodeTool(workspace).run(pattern=".", max_results=2)

        assert len(result.data) == 2
        assert result.metadata["truncated"] is True


class TestDatabaseGuard:
    """The read-only guard never reads the prompt, only the SQL."""

    def test_accepts_a_single_select(self) -> None:
        assert assert_read_only("SELECT Id FROM Orders;").startswith("SELECT")

    def test_accepts_a_common_table_expression(self) -> None:
        assert assert_read_only("WITH x AS (SELECT 1) SELECT * FROM x")

    @pytest.mark.parametrize(
        "statement",
        [
            "DELETE FROM Orders",
            "UPDATE Orders SET Total = 0",
            "DROP TABLE Orders",
            "TRUNCATE TABLE Orders",
            "EXEC sp_who",
            "SELECT 1; DROP TABLE Orders",
        ],
    )
    def test_refuses_anything_that_writes(self, statement: str) -> None:
        with pytest.raises(ToolError):
            assert_read_only(statement)

    def test_ignores_a_comment_that_hides_a_write(self) -> None:
        # The keyword is inside a comment, so the statement is still a plain read.
        assert assert_read_only("SELECT 1 -- DROP TABLE Orders")

    def test_the_tool_returns_rows_from_the_injected_runner(self) -> None:
        tool = QueryDatabaseTool(lambda sql, parameters=None: [{"Id": 1}, {"Id": 2}])

        result = tool.run(sql="SELECT Id FROM Orders")

        assert result.ok
        assert result.data["row_count"] == 2

    def test_the_tool_reports_a_driver_error_as_a_failure(self) -> None:
        def boom(sql: str, parameters: list | None = None) -> list:
            raise RuntimeError("connection refused")

        result = QueryDatabaseTool(boom).run(sql="SELECT 1")

        assert not result.ok
        assert "connection refused" in (result.error or "")


class TestOutputParsers:
    def test_extracts_compiler_errors(self) -> None:
        outcome = CommandOutcome(
            command="dotnet build",
            exit_code=1,
            stdout="Order.cs(42,17): error CS0103: The name 'x' does not exist\n",
            stderr="",
        )

        errors = parse_build_errors(outcome)

        assert errors == [
            {
                "file": "Order.cs",
                "line": 42,
                "code": "CS0103",
                "message": "The name 'x' does not exist",
            }
        ]

    def test_deduplicates_repeated_errors(self) -> None:
        line = "Order.cs(42,17): error CS0103: repeated\n"
        outcome = CommandOutcome("dotnet build", 1, line * 3, "")

        assert len(parse_build_errors(outcome)) == 1

    def test_reads_the_test_summary_line(self) -> None:
        outcome = CommandOutcome(
            command="dotnet test",
            exit_code=1,
            stdout="Failed! - Failed: 2, Passed: 10, Skipped: 1, Total: 13",
            stderr="",
        )

        summary = parse_test_summary(outcome)

        assert summary == {"passed": 10, "failed": 2, "skipped": 1, "success": False}

    def test_falls_back_to_the_exit_code_for_an_unknown_runner(self) -> None:
        summary = parse_test_summary(CommandOutcome("pytest", 0, "3 passed", ""))

        assert summary["success"] is True
        assert summary["passed"] is None  # unknown, and reported as unknown


class TestToolContract:
    def test_the_anthropic_schema_closes_itself_when_strict(self) -> None:
        tool = FunctionTool(
            "noop",
            "Does nothing.",
            {"type": "object", "properties": {"a": {"type": "string"}}, "required": ["a"]},
            lambda a: a,
        )

        schema = tool.to_anthropic_schema()

        assert schema["strict"] is True
        assert schema["input_schema"]["additionalProperties"] is False

    def test_a_raising_tool_returns_a_failure_rather_than_propagating(self) -> None:
        def boom() -> None:
            raise ValueError("nope")

        result = FunctionTool("boom", "Raises.", {"type": "object", "properties": {}}, boom).run()

        assert isinstance(result, ToolResult)
        assert not result.ok
        assert "ValueError" in (result.error or "")

    def test_unknown_arguments_are_dropped_rather_than_passed_through(self) -> None:
        seen: dict = {}

        def record(a: str) -> str:
            seen["a"] = a
            return a

        tool = FunctionTool(
            "record",
            "Records.",
            {"type": "object", "properties": {"a": {"type": "string"}}, "required": ["a"]},
            record,
        )

        assert tool.run(a="x", unexpected="y").ok
        assert seen == {"a": "x"}

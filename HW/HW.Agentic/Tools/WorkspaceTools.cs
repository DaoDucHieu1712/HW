using System.Text.Json;
using HW.Agentic.Abstractions.Development;
using HW.Agentic.Core;

namespace HW.Agentic.Tools;

/// <summary>Locates files without reading them, so an agent can orient before spending context.</summary>
public sealed class ListFilesTool : AgentToolBase
{
    private readonly IWorkspaceReader _workspace;

    public ListFilesTool(IWorkspaceReader workspace, AgentLoopOptions options) : base(options)
        => _workspace = workspace;

    public override string Name => "list_files";

    public override string Description =>
        "List source files in the repository, optionally filtered by a glob such as " +
        "\"HW.Application/**/*.cs\". Returns paths with line counts and sizes, not contents. Use it " +
        "to find where something lives before reading it — reading a file you guessed at wastes a turn.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "glob": {
              "type": "string",
              "description": "Glob relative to the repository root, e.g. \"HW.Domain/Entities/*.cs\". Omit for everything."
            },
            "limit": {
              "type": "integer",
              "description": "Maximum paths to return. Defaults to 50."
            }
          },
          "additionalProperties": false
        }
        """;

    public override Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var limit = Math.Clamp(OptionalInt(input, "limit") ?? 50, 1, 200);
        var files = _workspace.List(OptionalString(input, "glob"), limit);

        if (files.Count == 0)
            return Task.FromResult("No files match that pattern.");

        return Task.FromResult(Serialize(new { root = _workspace.Root, count = files.Count, files }));
    }
}

/// <summary>Reads a file with line numbers, so anything the agent says about it can cite a line.</summary>
public sealed class ReadFileTool : AgentToolBase
{
    private readonly IWorkspaceReader _workspace;

    public ReadFileTool(IWorkspaceReader workspace, AgentLoopOptions options) : base(options)
        => _workspace = workspace;

    public override string Name => "read_file";

    public override string Description =>
        "Read a source file from the repository, with 1-based line numbers prefixed. Pass fromLine " +
        "and toLine to read a window of a large file rather than all of it. Always read a file " +
        "before proposing a patch to it — propose_patch replaces the whole file, so a patch written " +
        "from memory silently deletes whatever you did not know was there.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Path relative to the repository root, e.g. \"HW.Domain/Entities/Vocab.cs\"."
            },
            "fromLine": {
              "type": "integer",
              "description": "First line to read, 1-based. Omit to start at the top."
            },
            "toLine": {
              "type": "integer",
              "description": "Last line to read, inclusive. Omit to read to the end."
            }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
        => await _workspace.ReadAsync(
            RequiredString(input, "path"),
            OptionalInt(input, "fromLine"),
            OptionalInt(input, "toLine"),
            ct);
}

/// <summary>Substring or regex search across the tree — how an agent finds a symbol it cannot guess the home of.</summary>
public sealed class SearchCodeTool : AgentToolBase
{
    private readonly IWorkspaceReader _workspace;

    public SearchCodeTool(IWorkspaceReader workspace, AgentLoopOptions options) : base(options)
        => _workspace = workspace;

    public override string Name => "search_code";

    public override string Description =>
        "Search the repository for a string or regular expression and get back matching files with " +
        "line numbers and the matching line. This is the fastest way to find where a type, a message " +
        "string from a log, or a column name is actually used. Narrow with a glob when a common word " +
        "would match everywhere.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "pattern": {
              "type": "string",
              "description": "Text to find, or a .NET regular expression when regex is true."
            },
            "regex": {
              "type": "boolean",
              "description": "True to treat the pattern as a regular expression. Defaults to false (plain substring)."
            },
            "glob": {
              "type": "string",
              "description": "Restrict the search, e.g. \"HW.Infrastructure/**/*.cs\"."
            },
            "limit": {
              "type": "integer",
              "description": "Maximum matches to return. Defaults to 40."
            }
          },
          "required": ["pattern"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var limit = Math.Clamp(OptionalInt(input, "limit") ?? 40, 1, 200);

        var matches = await _workspace.SearchAsync(
            RequiredString(input, "pattern"),
            OptionalString(input, "glob"),
            OptionalBool(input, "regex"),
            limit,
            ct);

        if (matches.Count == 0)
            return "No matches.";

        return Serialize(new { count = matches.Count, matches });
    }
}
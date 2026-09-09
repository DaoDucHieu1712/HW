using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HW.Application.Abstractions.Development;

namespace HW.Application.Agents.Tools;

/// <summary>
/// The only way an agent can affect the source tree — and it does not write anything.
///
/// <para>
/// A proposal is queued for a human to approve. The tool is marked mutating so a read-only run
/// cannot even see it, and the hash of each file as it was read is recorded here so approval can
/// refuse a patch whose file moved underneath it.
/// </para>
/// </summary>
public sealed class ProposePatchTool : AgentToolBase
{
    private readonly IPatchProposalStore _store;
    private readonly IWorkspaceReader _workspace;

    public ProposePatchTool(IPatchProposalStore store, IWorkspaceReader workspace, AgentLoopOptions options)
        : base(options)
    {
        _store = store;
        _workspace = workspace;
    }

    public override string Name => "propose_patch";

    public override string Description =>
        "Propose an edit to one or more files. Nothing is written: the proposal is queued for a " +
        "human to approve or reject, and you get back its id to report. Give the COMPLETE new " +
        "contents of each file — it replaces the file wholesale — so read every file with read_file " +
        "first and change only what needs changing. Explain in the rationale what was wrong and why " +
        "this fixes it; that is what the reviewer decides on.";

    public override bool IsMutating => true;

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "title": {
              "type": "string",
              "description": "One line naming the change, e.g. \"Fix N+1 query in GetVocabsQuery\"."
            },
            "rationale": {
              "type": "string",
              "description": "What is wrong, the evidence for it, and why this change fixes it."
            },
            "files": {
              "type": "array",
              "description": "The files to change.",
              "items": {
                "type": "object",
                "properties": {
                  "path": {
                    "type": "string",
                    "description": "Path relative to the repository root."
                  },
                  "newContent": {
                    "type": "string",
                    "description": "The complete new contents of the file, not a diff and not a fragment."
                  }
                },
                "required": ["path", "newContent"]
              }
            }
          },
          "required": ["title", "rationale", "files"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var title = RequiredString(input, "title");
        var rationale = RequiredString(input, "rationale");
        var entries = OptionalArray(input, "files");

        if (entries.Count == 0)
            throw new ArgumentException("Argument 'files' must contain at least one file to change.");

        var files = new List<PatchFile>(entries.Count);

        foreach (var entry in entries)
        {
            var path = RequiredString(entry, "path");

            // Empty content is legal JSON but almost never the intent — it is what a truncated
            // generation produces, and it would blank a source file on approval.
            var newContent = entry.TryGetProperty("newContent", out var content) && content.ValueKind == JsonValueKind.String
                ? content.GetString() ?? string.Empty
                : throw new ArgumentException($"File '{path}' is missing 'newContent'.");

            if (string.IsNullOrEmpty(newContent))
                throw new ArgumentException($"File '{path}' has empty 'newContent'. Send the complete file, not a fragment.");

            var exists = _workspace.Exists(path);
            var original = exists ? await _workspace.ReadRawAsync(path, ct) : null;

            files.Add(new PatchFile(
                path,
                original is null ? null : Hash(original),
                newContent,
                UnifiedDiff.Build(path, original, newContent)));
        }

        var proposal = _store.Add(AgentName(input), title, rationale, files);

        return Serialize(new
        {
            proposalId = proposal.Id,
            status = proposal.Status.ToString(),
            filesChanged = files.Select(file => file.Path),
            note = "Queued for human approval. Nothing has been written to disk. Report this id to the user.",
        });
    }

    /// <summary>
    /// The agent may name itself so a proposal can be traced back to which specialist produced it;
    /// the loop does not thread its identity into tool arguments.
    /// </summary>
    private static string AgentName(JsonElement input) => OptionalString(input, "agent") ?? "agent";

    private static string Hash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}

/// <summary>
/// Renders a unified diff for a human reviewer.
///
/// <para>
/// A plain longest-common-subsequence diff, not a fuzzy matcher: it is only ever displayed, never
/// applied — approval writes <see cref="PatchFile.NewContent"/> — so it has to be readable, not
/// re-appliable.
/// </para>
/// </summary>
public static class UnifiedDiff
{
    public static string Build(string path, string? original, string updated)
    {
        var before = Split(original);
        var after = Split(updated);
        var lcs = LongestCommonSubsequence(before, after);

        var builder = new StringBuilder();
        builder.Append("--- a/").AppendLine(path);
        builder.Append("+++ b/").AppendLine(path);

        int i = 0, j = 0, k = 0;

        while (i < before.Length || j < after.Length)
        {
            if (k < lcs.Count && i < before.Length && j < after.Length
                && before[i] == lcs[k] && after[j] == lcs[k])
            {
                builder.Append("  ").AppendLine(before[i]);
                i++; j++; k++;
                continue;
            }

            if (i < before.Length && (k >= lcs.Count || before[i] != lcs[k]))
            {
                builder.Append("- ").AppendLine(before[i]);
                i++;
                continue;
            }

            if (j < after.Length)
            {
                builder.Append("+ ").AppendLine(after[j]);
                j++;
            }
        }

        return builder.ToString();
    }

    private static string[] Split(string? text)
        => string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Split('\n');

    private static List<string> LongestCommonSubsequence(string[] before, string[] after)
    {
        // O(n·m) in both time and memory. Fine for a source file; a caller diffing something huge
        // would want a different algorithm, and does not exist here.
        var table = new int[before.Length + 1, after.Length + 1];

        for (var i = before.Length - 1; i >= 0; i--)
        {
            for (var j = after.Length - 1; j >= 0; j--)
            {
                table[i, j] = before[i] == after[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var sequence = new List<string>();
        int x = 0, y = 0;

        while (x < before.Length && y < after.Length)
        {
            if (before[x] == after[y])
            {
                sequence.Add(before[x]);
                x++; y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return sequence;
    }
}

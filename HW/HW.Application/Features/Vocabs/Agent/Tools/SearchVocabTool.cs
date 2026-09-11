using System.Text.Json;
using HW.Application.Features.Vocabs.Queries.GetVocabs;
using MediatR;

using HW.Agentic.Core;

namespace HW.Application.Features.Vocabs.Agent.Tools;

/// <summary>
/// The agent's way into the saved list. This is the tool it should reach for before writing
/// anything, so the description leads with that.
/// </summary>
public sealed class SearchVocabTool : VocabAgentToolBase
{
    public SearchVocabTool(ISender sender, AgentLoopOptions options) : base(sender, options) { }

    public override string Name => "search_vocab";

    public override string Description =>
        "Search the user's saved vocabulary. Use this before saving a word, to check whether it is " +
        "already there, and whenever the user refers to words they have noted. Omit 'search' to " +
        "browse the newest entries. Returns each match with its id, meaning, review stage, and " +
        "next review date; the id is what the other vocab tools take.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "search": {
              "type": "string",
              "description": "Substring matched against the word itself, case-insensitive. Omit to list the most recently added words."
            },
            "notedFrom": {
              "type": "string",
              "description": "Only words noted on or after this date (ISO-8601, e.g. 2026-01-31)."
            },
            "notedTo": {
              "type": "string",
              "description": "Only words noted on or before this date (ISO-8601)."
            },
            "page": {
              "type": "integer",
              "description": "1-based page number. Defaults to 1; increase it to see further matches."
            },
            "pageSize": {
              "type": "integer",
              "description": "Matches per page. Defaults to 20."
            }
          },
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(OptionalInt(input, "pageSize") ?? 20, 1, Options.MaxToolResultItems);
        var page = Math.Max(OptionalInt(input, "page") ?? 1, 1);

        var result = await Sender.Send(
            new GetVocabsQuery(
                OptionalString(input, "search"),
                OptionalDate(input, "notedFrom"),
                OptionalDate(input, "notedTo"),
                page,
                pageSize),
            ct);

        if (result.Items.Count == 0)
            return "No saved words match that search.";

        return Serialize(new
        {
            page,
            pageSize,
            result.TotalCount,
            result.HasNextPage,
            words = result.Items,
        });
    }
}

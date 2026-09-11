using System.Text.Json;
using HW.Application.Features.Vocabs.Commands.CreateVocab;
using HW.Application.Features.Vocabs.Queries.GetVocabs;
using MediatR;

using HW.Agentic.Core;

namespace HW.Application.Features.Vocabs.Agent.Tools;

/// <summary>
/// Adds a word to the list and starts its review schedule.
///
/// <para>
/// Nothing in the domain stops the same word being saved twice, so this tool checks first and
/// refuses the duplicate. Enforcing it here rather than trusting the prompt means a model that
/// skipped <c>search_vocab</c> still cannot litter the list.
/// </para>
/// </summary>
public sealed class SaveVocabTool : VocabAgentToolBase
{
    public SaveVocabTool(ISender sender, AgentLoopOptions options) : base(sender, options) { }

    public override string Name => "save_vocab";

    public override string Description =>
        "Save a new word to the user's vocabulary and start its review schedule (stage 0, first " +
        "review in 3 days). Search first — saving a word that is already there is rejected. Write " +
        "the meaning yourself and make it worth re-reading: part of speech, a Vietnamese gloss, a " +
        "natural example sentence, and the collocations or preposition the word travels with.";

    public override bool IsMutating => true;

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "word": {
              "type": "string",
              "description": "The English word or phrase, as it should be studied. Max 200 characters."
            },
            "content": {
              "type": "string",
              "description": "The meaning entry: part of speech, Vietnamese gloss, an example sentence, and usual collocations."
            }
          },
          "required": ["word", "content"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var word = RequiredString(input, "word");
        var content = RequiredString(input, "content");

        var existing = await Sender.Send(new GetVocabsQuery(word, null, null, 1, Options.MaxToolResultItems), ct);

        var duplicate = existing.Items
            .FirstOrDefault(item => string.Equals(item.Word, word, StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null)
        {
            return $"'{word}' is already saved (id {duplicate.Id}, review stage {duplicate.ReviewStage}). " +
                   "Use update_vocab to change its meaning instead of saving it again.";
        }

        await Sender.Send(new CreateVocabCommand(word, content), ct);

        // The create command returns nothing, so the id is read back rather than guessed — the model
        // usually wants it immediately, to keep working with the word it just saved.
        var saved = await Sender.Send(new GetVocabsQuery(word, null, null, 1, Options.MaxToolResultItems), ct);

        var created = saved.Items
            .FirstOrDefault(item => string.Equals(item.Word, word, StringComparison.OrdinalIgnoreCase));

        return created is null
            ? $"Saved '{word}'."
            : Serialize(new { saved = created });
    }
}

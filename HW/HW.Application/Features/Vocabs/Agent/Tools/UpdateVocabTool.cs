using System.Text.Json;
using HW.Application.Features.Vocabs.Commands.UpdateVocab;
using HW.Application.Features.Vocabs.Queries.GetVocabById;
using MediatR;

using HW.Application.Agents;

namespace HW.Application.Features.Vocabs.Agent.Tools;

/// <summary>
/// Edits a saved word. Both fields are optional and only what is supplied is written, so the model
/// can correct a meaning without having to restate the word.
/// </summary>
public sealed class UpdateVocabTool : VocabAgentToolBase
{
    public UpdateVocabTool(ISender sender, AgentLoopOptions options) : base(sender, options) { }

    public override string Name => "update_vocab";

    public override string Description =>
        "Edit a saved word's spelling or meaning. Send only the fields you are changing; anything " +
        "omitted is left as it is. This does not touch the review schedule — use mark_vocab_reviewed " +
        "for that. Read the word with get_vocab first so you are not overwriting a better entry.";

    public override bool IsMutating => true;

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "Id of the saved word to edit."
            },
            "word": {
              "type": "string",
              "description": "Corrected spelling of the word. Omit to leave it unchanged."
            },
            "content": {
              "type": "string",
              "description": "Replacement meaning entry. Replaces the old one wholesale, so include everything worth keeping."
            }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequiredString(input, "id");
        var word = OptionalString(input, "word");
        var content = OptionalString(input, "content");

        if (word is null && content is null)
            throw new ArgumentException("Supply at least one of 'word' or 'content' — there is nothing to update otherwise.");

        await Sender.Send(new UpdateVocabCommand(id, word, content), ct);

        var updated = await Sender.Send(new GetVocabByIdQuery(id), ct);

        return Serialize(new { updated });
    }
}

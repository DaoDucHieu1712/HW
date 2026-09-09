using System.Text.Json;
using HW.Application.Features.Vocabs.Commands.ReviewVocab;
using HW.Application.Features.Vocabs.Queries.GetVocabById;
using MediatR;

using HW.Application.Agents;

namespace HW.Application.Features.Vocabs.Agent.Tools;

/// <summary>
/// Advances one word along the spaced-repetition ladder.
///
/// <para>
/// This is the sharpest tool the agent has: the step is one-way, and a word pushed to Mastered
/// stops appearing in reviews. The description spells out when it is warranted, because the failure
/// here is silent — the user only notices weeks later when the word never comes back.
/// </para>
/// </summary>
public sealed class ReviewVocabTool : VocabAgentToolBase
{
    public ReviewVocabTool(ISender sender, AgentLoopOptions options) : base(sender, options) { }

    public override string Name => "mark_vocab_reviewed";

    public override string Description =>
        "Advance a word one review stage (0 New → 1 Reviewed → 2 Reinforced → 3 Mastered) and " +
        "reschedule it. Call this only after the user has actually recalled the word correctly — " +
        "not because they asked to see it, and not for a word they got wrong. The step cannot be " +
        "undone, and a word that reaches Mastered stops coming up for review. One word per call.";

    public override bool IsMutating => true;

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "Id of the word the user just recalled correctly."
            }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequiredString(input, "id");

        await Sender.Send(new ReviewVocabCommand(id), ct);

        var vocab = await Sender.Send(new GetVocabByIdQuery(id), ct);

        return Serialize(new
        {
            vocab.Word,
            vocab.ReviewStage,
            vocab.NextReviewAt,
            vocab.IsCompleted,
        });
    }
}

using System.Text.Json;
using HW.Application.Features.Vocabs.Queries.GetFlashCards;
using MediatR;

using HW.Agentic.Core;

namespace HW.Application.Features.Vocabs.Agent.Tools;

/// <summary>
/// Draws a shuffled deck the agent can quiz from. Reading a card is not reviewing it — advancing
/// the schedule stays with <see cref="ReviewVocabTool"/>, once the user has actually answered.
/// </summary>
public sealed class FlashCardsTool : VocabAgentToolBase
{
    public FlashCardsTool(ISender sender, AgentLoopOptions options) : base(sender, options) { }

    public override string Name => "draw_flashcards";

    public override string Description =>
        "Draw a shuffled deck of saved words to quiz the user on. Set dueOnly to true for a real " +
        "review session, or filter by review stage to drill one level. Drawing a card does not " +
        "count as reviewing it — quiz the user card by card, and call mark_vocab_reviewed only for " +
        "the ones they get right.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "count": {
              "type": "integer",
              "description": "How many cards to draw. Defaults to 10."
            },
            "reviewStage": {
              "type": "integer",
              "description": "Only draw words at this stage: 0 New, 1 Reviewed, 2 Reinforced, 3 Mastered.",
              "enum": [0, 1, 2, 3]
            },
            "dueOnly": {
              "type": "boolean",
              "description": "True to draw only from words due for review today. Defaults to false (draw from everything)."
            }
          },
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var count = Math.Clamp(OptionalInt(input, "count") ?? 10, 1, Options.MaxToolResultItems);

        var deck = await Sender.Send(
            new GetFlashCardsQuery(count, OptionalInt(input, "reviewStage"), OptionalBool(input, "dueOnly")),
            ct);

        if (deck.Count == 0)
            return "No saved words match those filters, so there is no deck to draw.";

        return Serialize(new { cards = deck });
    }
}

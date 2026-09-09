using System.Text.Json;
using HW.Application.Features.Vocabs.Queries.GetVocabById;
using MediatR;

using HW.Application.Agents;

namespace HW.Application.Features.Vocabs.Agent.Tools;

public sealed class GetVocabTool : VocabAgentToolBase
{
    public GetVocabTool(ISender sender, AgentLoopOptions options) : base(sender, options) { }

    public override string Name => "get_vocab";

    public override string Description =>
        "Read one saved word in full by its id. Use it to confirm the current meaning and review " +
        "stage before editing or before marking the word reviewed. Ids come from search_vocab, " +
        "daily_mission, or draw_flashcards — never invent one.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "Id of the saved word, as returned by another vocab tool."
            }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var vocab = await Sender.Send(new GetVocabByIdQuery(RequiredString(input, "id")), ct);

        return Serialize(vocab);
    }
}

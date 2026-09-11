using System.Text.Json;
using HW.Application.Features.Vocabs.Queries.GenerateVocabExam;
using MediatR;

using HW.Agentic.Core;

namespace HW.Application.Features.Vocabs.Agent.Tools;

/// <summary>
/// Builds a mixed exam from the user's own words, with distractors drawn from their other saved
/// meanings — harder, and more useful, than options the model would invent.
/// </summary>
public sealed class GenerateExamTool : VocabAgentToolBase
{
    public GenerateExamTool(ISender sender, AgentLoopOptions options) : base(sender, options) { }

    public override string Name => "generate_exam";

    public override string Description =>
        "Build a mixed exam from the user's saved words: multiple choice (type 0), true/false " +
        "(type 1), and written recall (type 2). Distractors are real meanings from their own list. " +
        "Ask the questions one at a time and mark the answers yourself — the exam is not stored, " +
        "and nothing here changes a review schedule.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "questionCount": {
              "type": "integer",
              "description": "How many questions to generate. Defaults to 5."
            },
            "notedFrom": {
              "type": "string",
              "description": "Only draw on words noted on or after this date (ISO-8601)."
            },
            "notedTo": {
              "type": "string",
              "description": "Only draw on words noted on or before this date (ISO-8601)."
            }
          },
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var questionCount = Math.Clamp(OptionalInt(input, "questionCount") ?? 5, 1, Options.MaxToolResultItems);

        var questions = await Sender.Send(
            new GenerateVocabExamQuery(questionCount, OptionalDate(input, "notedFrom"), OptionalDate(input, "notedTo")),
            ct);

        return Serialize(new { questions });
    }
}

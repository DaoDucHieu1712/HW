using System.Text.Json;
using HW.Application.Features.Vocabs.Queries.GetDailyMission;
using MediatR;

using HW.Agentic.Core;

namespace HW.Application.Features.Vocabs.Agent.Tools;

/// <summary>
/// Today's spaced-repetition queue. Answering "what should I study today?" without calling this is
/// the mistake worth designing against, so the description says so outright.
/// </summary>
public sealed class DailyMissionTool : VocabAgentToolBase
{
    public DailyMissionTool(ISender sender, AgentLoopOptions options) : base(sender, options) { }

    public override string Name => "daily_mission";

    public override string Description =>
        "List the words due for review today, oldest due date first. Call this whenever the user " +
        "asks what to study, how much is left today, or asks to start a review session — do not " +
        "answer those from memory. Returns an empty list when nothing is due.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var due = await Sender.Send(new GetDailyMissionQuery(), ct);

        if (due.Count == 0)
            return "Nothing is due for review today.";

        // The queue can outgrow a useful turn. Cutting it here — and saying so — keeps the model
        // from planning a fifty-word session it will never finish.
        var shown = due.Take(Options.MaxToolResultItems).ToList();

        return Serialize(new
        {
            dueCount = due.Count,
            shownCount = shown.Count,
            words = shown,
        });
    }
}

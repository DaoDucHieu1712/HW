using HW.Agentic.Abstractions;
using HW.Agentic.Core;
using HW.Application.Features.Vocabs.Dtos;

namespace HW.Application.Features.Vocabs.Agent;

/// <summary>
/// The vocabulary coach. All the loop mechanics live in <see cref="EngineerLoop"/> — this only
/// describes the agent (its instructions, its tools, its provider) and maps the neutral run result
/// onto the vocab API's own contract, so that contract stays stable while the engine evolves.
/// </summary>
public sealed class VocabAgentLoop : IVocabAgent
{
    /// <summary>
    /// Named explicitly rather than left empty. An agent that inherits every registered tool would
    /// silently gain the developer tools — file reads, SQL traces — the moment those were added.
    /// </summary>
    public static readonly IReadOnlyList<string> ToolNames =
    [
        "search_vocab",
        "get_vocab",
        "daily_mission",
        "draw_flashcards",
        "generate_exam",
        "save_vocab",
        "update_vocab",
        "mark_vocab_reviewed",
    ];

    private readonly IEngineerLoop _loop;

    public VocabAgentLoop(IEngineerLoop loop) => _loop = loop;

    public async Task<VocabAgentDtos.VocabAgentResponseDto> RunAsync(
        VocabAgentDtos.AskVocabAgentRequestDto request,
        CancellationToken ct = default)
    {
        var history = request.History?
            .Select(turn => new AgentConversationTurn(turn.Role, turn.Text))
            .ToList();

        var result = await _loop.RunAsync(
            new AgentRunRequest(
                Definition(request.EnableWebLookup),
                request.Question,
                history,
                request.AllowMutations),
            ct);

        return new VocabAgentDtos.VocabAgentResponseDto(
            result.Answer,
            result.Iterations,
            result.BudgetExhausted,
            result.StopReason,
            result.Steps
                .Select(step => new VocabAgentDtos.VocabAgentStepDto(
                    step.Iteration, step.Tool, step.Input, step.Output, step.IsError, step.ElapsedMs))
                .ToList(),
            new VocabAgentDtos.VocabAgentUsageDto(
                result.Usage.InputTokens,
                result.Usage.OutputTokens,
                result.Usage.CacheReadInputTokens,
                result.Usage.CacheCreationInputTokens));
    }

    public static AgentDefinition Definition(bool enableWebLookup = false) => new(
        Name: "vocab-coach",
        Description: "Looks words up, saves them, and runs spaced-repetition review sessions over the user's own vocabulary list.",
        SystemPrompt: VocabAgentPrompt.System,
        Tools: ToolNames,
        Provider: LlmProvider.Claude,
        EnableWebLookup: enableWebLookup);
}

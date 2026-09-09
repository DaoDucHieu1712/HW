namespace HW.Application.Features.Vocabs.Dtos;

public static class VocabAgentDtos
{
    /// <param name="AllowMutations">
    /// Lets the agent write — add a word, edit a meaning, advance a review stage. Turn it off for a
    /// pure lookup session and the write tools are not even advertised to the model.
    /// </param>
    /// <param name="EnableWebLookup">
    /// Adds the provider's server-side web search, for words that are not in the vocab list yet.
    /// </param>
    /// <param name="History">Earlier turns of the same conversation. The API itself is stateless.</param>
    public record AskVocabAgentRequestDto(
        string Question,
        bool AllowMutations = true,
        bool EnableWebLookup = false,
        List<VocabAgentTurnDto>? History = null);

    /// <param name="Role">"user" or "assistant".</param>
    public record VocabAgentTurnDto(string Role, string Text);

    /// <summary>One tool call the loop executed — the audit trail of how the answer was reached.</summary>
    public record VocabAgentStepDto(
        int Iteration,
        string Tool,
        string Input,
        string Output,
        bool IsError,
        long ElapsedMs);

    public record VocabAgentUsageDto(
        long InputTokens,
        long OutputTokens,
        long CacheReadInputTokens,
        long CacheCreationInputTokens);

    /// <param name="BudgetExhausted">
    /// True when the loop hit its iteration cap and had to force a wrap-up answer, so the reply may
    /// be based on incomplete work.
    /// </param>
    public record VocabAgentResponseDto(
        string Answer,
        int Iterations,
        bool BudgetExhausted,
        string StopReason,
        List<VocabAgentStepDto> Steps,
        VocabAgentUsageDto Usage);
}

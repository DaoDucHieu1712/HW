namespace HW.Application.Abstractions.AI;

/// <summary>
/// One round-trip to the model. This is the only seam the loop has on an LLM provider, so the
/// Application layer never references a vendor SDK — <c>HW.Infrastructure.AI</c> supplies the adapter.
/// </summary>
public interface IAgentChatClient
{
    /// <summary>Which vendor this adapter talks to. The resolver keys on it.</summary>
    LlmProvider Provider { get; }

    Task<AgentTurn> CompleteAsync(AgentCompletionRequest request, CancellationToken ct = default);
}

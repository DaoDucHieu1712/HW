namespace HW.Agentic.Abstractions;

/// <summary>
/// One round-trip to the model. This is the only seam the loop has on an LLM provider: nothing above
/// it names a vendor, and <c>HW.Agentic.Providers</c> supplies the adapters that do.
/// </summary>
public interface IAgentChatClient
{
    /// <summary>Which vendor this adapter talks to. The resolver keys on it.</summary>
    LlmProvider Provider { get; }

    Task<AgentTurn> CompleteAsync(AgentCompletionRequest request, CancellationToken ct = default);
}

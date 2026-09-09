using HW.Application.Abstractions.AI;

namespace HW.Application.Agents;

/// <param name="Role">"user" or "assistant".</param>
public sealed record AgentConversationTurn(string Role, string Text);

/// <summary>One tool call the loop executed — the audit trail of how an answer was reached.</summary>
public sealed record AgentStep(
    int Iteration,
    string Tool,
    string Input,
    string Output,
    bool IsError,
    long ElapsedMs);

/// <param name="AllowMutations">
/// False hides every write tool from the model, rather than rejecting the call afterwards.
/// </param>
/// <param name="ProviderOverride">
/// Runs this agent on a different vendor than its definition names. This is what a fan-out step
/// varies while holding the role and the prompt constant.
/// </param>
public sealed record AgentRunRequest(
    AgentDefinition Agent,
    string Task,
    IReadOnlyList<AgentConversationTurn>? History = null,
    bool AllowMutations = true,
    LlmProvider? ProviderOverride = null);

/// <param name="BudgetExhausted">
/// True when the loop hit its iteration cap and had to force a wrap-up, so the answer may rest on
/// incomplete work.
/// </param>
public sealed record AgentRunResult(
    string Agent,
    LlmProvider Provider,
    string Answer,
    int Iterations,
    bool BudgetExhausted,
    string StopReason,
    IReadOnlyList<AgentStep> Steps,
    AgentUsage Usage,
    long ElapsedMs);

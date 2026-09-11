using HW.Agentic.Abstractions;
using HW.Agentic.Core;

namespace HW.Application.Agents.Dtos;

public static class AgentDtos
{
    /// <param name="Role">"user" or "assistant".</param>
    public record AgentTurnDto(string Role, string Text);

    /// <param name="Agent">Name of a registered agent — see GET /api/dev-agent/agents.</param>
    /// <param name="Provider">
    /// "Claude", "OpenAI", or "Gemini" to run this agent somewhere other than its default. Null uses
    /// the agent's own.
    /// </param>
    /// <param name="AllowMutations">
    /// False hides every write tool, so the agent can look but not propose. Worth setting for a
    /// question you only want answered.
    /// </param>
    public record AskAgentRequestDto(
        string Agent,
        string Task,
        string? Provider = null,
        bool AllowMutations = true,
        List<AgentTurnDto>? History = null);

    public record RunWorkflowRequestDto(string Input);

    /// <param name="ConversationId">
    /// Omit to start a new conversation; the response returns the id to send back next turn.
    /// </param>
    /// <param name="Agent">
    /// Who to talk to. Defaults to the manager, which is the one that coordinates the specialists —
    /// name a specialist only when you already know which one you want.
    /// </param>
    public record ChatRequestDto(
        string Message,
        string? ConversationId = null,
        string? Agent = null,
        string? Provider = null,
        bool AllowMutations = true);

    /// <summary>One specialist run the manager commissioned to answer this turn.</summary>
    public record DelegationSummaryDto(
        string Agent,
        string Provider,
        string Task,
        string Answer,
        bool Failed,
        string? Error,
        int Iterations,
        bool BudgetExhausted,
        IReadOnlyList<AgentStep> Steps,
        AgentUsage Usage,
        long ElapsedMs);

    /// <param name="Delegations">
    /// The work behind the answer. Empty when the agent answered on its own.
    /// </param>
    /// <param name="Steps">The manager's own tool calls — the delegations it made, at the loop level.</param>
    /// <param name="Usage">Tokens for the whole turn: this agent plus every specialist it called.</param>
    public record ChatResponseDto(
        string ConversationId,
        string Agent,
        string Provider,
        string Answer,
        int Iterations,
        bool BudgetExhausted,
        string StopReason,
        IReadOnlyList<DelegationSummaryDto> Delegations,
        IReadOnlyList<AgentStep> Steps,
        AgentUsage Usage,
        long ElapsedMs);

    public record RejectPatchRequestDto(string? Reason);

    /// <param name="Path">Path relative to the repository root.</param>
    /// <param name="NewContent">
    /// The complete new contents of the file. Whole-file replacement, matching what
    /// <c>propose_patch</c> sends — see <c>PatchFile.NewContent</c> for why this is not a diff.
    /// </param>
    public record CreatePatchFileDto(string Path, string NewContent);

    /// <summary>
    /// A patch proposed from outside the in-process agents — the LangGraph loop in <c>agentic/</c>
    /// submitting a change it has already built, tested and reviewed in its own worktree.
    /// </summary>
    /// <remarks>
    /// This shares the existing queue rather than adding a second one, so every pending edit in the
    /// system is visible in one place and goes through the same approval, the same staleness check,
    /// and the same applier. The proposer is untrusted either way: nothing here writes to disk.
    /// </remarks>
    /// <param name="Agent">Who is proposing. Recorded so the queue says where a patch came from.</param>
    public record CreatePatchRequestDto(
        string Agent,
        string Title,
        string Rationale,
        List<CreatePatchFileDto> Files);

    public record AgentSummaryDto(
        string Name,
        string Description,
        string Provider,
        string? Model,
        IReadOnlyList<string> Tools,
        int? MaxIterations);

    public record WorkflowSummaryDto(string Name, string Description, IReadOnlyList<WorkflowStepSummaryDto> Steps);

    /// <param name="Kind">"agent" or "parallel".</param>
    /// <param name="Branches">For a parallel step, the agent and provider each branch runs on.</param>
    public record WorkflowStepSummaryDto(
        string Id,
        string Kind,
        string? Agent,
        IReadOnlyList<string>? Branches,
        string? SynthesisAgent);

    public record ProvidersDto(IReadOnlyList<string> Available, IReadOnlyList<string> Unavailable);

    /// <summary>
    /// Parses the provider name a caller sent. The parsing itself belongs to the runtime — the
    /// delegation tools read the same names off model-written JSON — so this is the API's spelling
    /// of <see cref="LlmProviders.Parse"/> rather than a second copy of the rule.
    /// </summary>
    public static LlmProvider? ParseProvider(string? value) => LlmProviders.Parse(value);
}

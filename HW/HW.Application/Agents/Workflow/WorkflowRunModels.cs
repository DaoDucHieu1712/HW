using HW.Application.Abstractions.AI;

namespace HW.Application.Agents.Workflow;

/// <param name="Failed">
/// The step or branch threw — a provider outage, an unconfigured key, a bad model name. Recorded
/// rather than propagated so the rest of the workflow can still produce something useful.
/// </param>
public sealed record WorkflowStepResult(
    string StepId,
    string Agent,
    LlmProvider Provider,
    string Output,
    bool Failed,
    string? Error,
    int Iterations,
    bool BudgetExhausted,
    IReadOnlyList<AgentStep> Steps,
    AgentUsage Usage,
    long ElapsedMs,
    IReadOnlyList<WorkflowStepResult> Branches);

/// <param name="Output">The last step's output — what a caller who wants one answer reads.</param>
public sealed record WorkflowRunResult(
    string Workflow,
    string Input,
    string Output,
    IReadOnlyList<WorkflowStepResult> Steps,
    AgentUsage Usage,
    long ElapsedMs);

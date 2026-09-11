using HW.Agentic.Abstractions;
using HW.Agentic.Core;

namespace HW.Agentic.Workflow;

/// <summary>
/// One step of a workflow.
///
/// <para>
/// <paramref name="Prompt"/> is a template. <c>{{input}}</c> is the workflow's own input, and
/// <c>{{stepId}}</c> is the output of an earlier step — that substitution is the entire mechanism by
/// which one specialist's findings reach the next. There is no shared mutable state beyond it,
/// which is what makes a parallel step safe to run in any order.
/// </para>
/// </summary>
public abstract record WorkflowStep(string Id, string Prompt);

/// <param name="Agent">Name of a registered agent.</param>
/// <param name="ProviderOverride">Runs this step on a different vendor than the agent's default.</param>
public sealed record AgentWorkflowStep(
    string Id,
    string Agent,
    string Prompt,
    LlmProvider? ProviderOverride = null,
    bool AllowMutations = true) : WorkflowStep(Id, Prompt);

/// <param name="Agent">Name of a registered agent.</param>
/// <param name="ProviderOverride">
/// The vendor this branch runs on. Setting it per branch is how the same agent and the same prompt
/// are put to Claude, GPT, and Gemini at once.
/// </param>
public sealed record WorkflowBranch(
    string Id,
    string Agent,
    LlmProvider? ProviderOverride = null,
    bool AllowMutations = false);

/// <summary>
/// Runs several branches at once on the same prompt, then optionally hands every branch's answer to
/// one more agent to reconcile.
///
/// <para>
/// Each branch gets its own DI scope and its own conversation, so branches share no database
/// context and no message history. That isolation is why a branch failing — a provider outage, a
/// missing key — costs its own result and not the workflow.
/// </para>
/// </summary>
/// <param name="SynthesisAgent">
/// Agent that merges the branch outputs. Null returns the branches unmerged, which is the right
/// choice when the caller wants to compare them rather than be told the answer.
/// </param>
public sealed record ParallelWorkflowStep(
    string Id,
    string Prompt,
    IReadOnlyList<WorkflowBranch> Branches,
    string? SynthesisAgent = null) : WorkflowStep(Id, Prompt);

public sealed record WorkflowDefinition(
    string Name,
    string Description,
    IReadOnlyList<WorkflowStep> Steps);
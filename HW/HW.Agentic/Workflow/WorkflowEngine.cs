using System.Diagnostics;
using System.Text;
using HW.Agentic.Abstractions;
using HW.Agentic.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HW.Agentic.Workflow;

public interface IWorkflowEngine
{
    Task<WorkflowRunResult> RunAsync(string workflowName, string input, CancellationToken ct = default);

    Task<WorkflowRunResult> RunAsync(WorkflowDefinition workflow, string input, CancellationToken ct = default);
}

/// <summary>
/// Runs a workflow: steps in order, each one's output available to the next by id, with parallel
/// steps fanning the same prompt out across agents or providers at once.
///
/// <para>
/// Every step and every branch runs in its own DI scope. That is not tidiness — it is what makes
/// the fan-out safe. The tools resolve scoped services, an EF Core <c>DbContext</c> among them, and
/// a DbContext cannot serve two concurrent operations; branches sharing the caller's scope would
/// corrupt each other under exactly the load this feature exists to create.
/// </para>
///
/// <para>
/// A failing step is recorded and the workflow continues. One provider being down, or one key being
/// unconfigured, should cost that branch's answer and not the whole run — and a synthesis over two
/// of three answers is still worth having, as long as it is told which one is missing.
/// </para>
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowCatalog _workflows;
    private readonly IAgentCatalog _agents;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(
        IServiceScopeFactory scopeFactory,
        IWorkflowCatalog workflows,
        IAgentCatalog agents,
        ILogger<WorkflowEngine> logger)
    {
        _scopeFactory = scopeFactory;
        _workflows = workflows;
        _agents = agents;
        _logger = logger;
    }

    public Task<WorkflowRunResult> RunAsync(string workflowName, string input, CancellationToken ct = default)
        => RunAsync(_workflows.Get(workflowName), input, ct);

    public async Task<WorkflowRunResult> RunAsync(
        WorkflowDefinition workflow,
        string input,
        CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var results = new List<WorkflowStepResult>(workflow.Steps.Count);

        // The blackboard: step id to output. The only thing that crosses between steps.
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = input,
        };

        var usage = AgentUsage.Zero;

        foreach (var step in workflow.Steps)
        {
            var prompt = Render(step.Prompt, outputs);

            var result = step switch
            {
                AgentWorkflowStep agentStep => await RunAgentStepAsync(agentStep, prompt, ct),
                ParallelWorkflowStep parallelStep => await RunParallelStepAsync(parallelStep, prompt, ct),
                _ => throw new NotSupportedException($"Unsupported workflow step type '{step.GetType().Name}'."),
            };

            results.Add(result);
            outputs[step.Id] = result.Output;
            usage += Total(result);
        }

        return new WorkflowRunResult(
            workflow.Name,
            input,
            results.Count == 0 ? string.Empty : results[^1].Output,
            results,
            usage,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private async Task<WorkflowStepResult> RunAgentStepAsync(
        AgentWorkflowStep step,
        string prompt,
        CancellationToken ct)
        => await RunOneAsync(step.Id, step.Agent, prompt, step.ProviderOverride, step.AllowMutations, ct);

    private async Task<WorkflowStepResult> RunParallelStepAsync(
        ParallelWorkflowStep step,
        string prompt,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        var branches = await Task.WhenAll(step.Branches.Select(branch =>
            RunOneAsync(branch.Id, branch.Agent, prompt, branch.ProviderOverride, branch.AllowMutations, ct)));

        var succeeded = branches.Where(branch => !branch.Failed).ToList();

        if (step.SynthesisAgent is null)
        {
            // No synthesis asked for: hand back every branch side by side and let the caller compare.
            return Combined(step.Id, branches, FormatBranches(branches), started);
        }

        if (succeeded.Count == 0)
        {
            return Combined(step.Id, branches, "Every branch of this step failed:\n\n" + FormatBranches(branches), started);
        }

        var synthesis = await RunOneAsync(
            step.Id + ":synthesis",
            step.SynthesisAgent,
            BuildSynthesisPrompt(prompt, branches),
            providerOverride: null,
            allowMutations: false,
            ct);

        var all = branches.Append(synthesis).ToList();

        return Combined(
            step.Id,
            all,
            synthesis.Failed ? FormatBranches(branches) : synthesis.Output,
            started);
    }

    private async Task<WorkflowStepResult> RunOneAsync(
        string stepId,
        string agentName,
        string prompt,
        LlmProvider? providerOverride,
        bool allowMutations,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var provider = providerOverride ?? LlmProvider.Claude;

        try
        {
            var agent = _agents.Get(agentName);
            provider = providerOverride ?? agent.Provider;

            // Its own scope, so the loop, its tools, and their DbContext belong to this branch alone.
            using var scope = _scopeFactory.CreateScope();
            var loop = scope.ServiceProvider.GetRequiredService<IEngineerLoop>();

            var result = await loop.RunAsync(
                new AgentRunRequest(agent, prompt, History: null, allowMutations, providerOverride),
                ct);

            return new WorkflowStepResult(
                stepId, result.Agent, result.Provider, result.Answer,
                Failed: false, Error: null,
                result.Iterations, result.BudgetExhausted, result.Steps, result.Usage, result.ElapsedMs,
                Branches: []);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller walked away. Abandoning the whole workflow is right here — recording a
            // cancellation as a step failure would let the run carry on spending on later steps.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow step {Step} ({Agent} on {Provider}) failed.", stepId, agentName, provider);

            return new WorkflowStepResult(
                stepId, agentName, provider,
                Output: $"This step failed: {ex.Message}",
                Failed: true, Error: ex.Message,
                Iterations: 0, BudgetExhausted: false, Steps: [], Usage: AgentUsage.Zero,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Branches: []);
        }
    }

    private static WorkflowStepResult Combined(
        string stepId,
        IReadOnlyList<WorkflowStepResult> branches,
        string output,
        long started)
        => new(
            stepId,
            Agent: string.Join(" + ", branches.Select(branch => branch.Agent).Distinct()),
            Provider: branches.Count > 0 ? branches[0].Provider : LlmProvider.Claude,
            output,
            Failed: branches.All(branch => branch.Failed),
            Error: null,
            Iterations: branches.Sum(branch => branch.Iterations),
            BudgetExhausted: branches.Any(branch => branch.BudgetExhausted),
            Steps: branches.SelectMany(branch => branch.Steps).ToList(),
            Usage: branches.Aggregate(AgentUsage.Zero, (total, branch) => total + branch.Usage),
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            branches);

    /// <summary>
    /// A parallel step's usage is already the sum of its branches, so counting the branches again
    /// would double it.
    /// </summary>
    private static AgentUsage Total(WorkflowStepResult result) => result.Usage;

    private static string FormatBranches(IReadOnlyList<WorkflowStepResult> branches)
    {
        var builder = new StringBuilder();

        foreach (var branch in branches)
        {
            builder.Append("### ").Append(branch.Agent).Append(" (").Append(branch.Provider).AppendLine(")");
            builder.AppendLine();
            builder.AppendLine(branch.Failed ? $"FAILED: {branch.Error}" : branch.Output);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Failed branches are named rather than dropped. A synthesiser told that two of three models
    /// answered can weigh that; one silently handed two answers will present them as the consensus.
    /// </summary>
    private static string BuildSynthesisPrompt(string prompt, IReadOnlyList<WorkflowStepResult> branches)
    {
        var builder = new StringBuilder();
        builder.AppendLine("The question put to every model was:");
        builder.AppendLine();
        builder.AppendLine(prompt);
        builder.AppendLine();
        builder.AppendLine("Their answers follow.");
        builder.AppendLine();
        builder.AppendLine(FormatBranches(branches));

        var failed = branches.Where(branch => branch.Failed).ToList();

        if (failed.Count > 0)
        {
            builder.AppendLine();
            builder.Append(failed.Count).Append(" of ").Append(branches.Count)
                .AppendLine(" branches failed and produced no answer. Take that into account rather than treating the rest as unanimous.");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Substitutes <c>{{stepId}}</c> placeholders with earlier outputs. An unresolved placeholder is
    /// left in place: blanking it would hand the model a prompt with a hole in it and no sign that
    /// something was meant to be there.
    /// </summary>
    private static string Render(string template, IReadOnlyDictionary<string, string> outputs)
    {
        var rendered = template;

        foreach (var (key, value) in outputs)
            rendered = rendered.Replace("{{" + key + "}}", value, StringComparison.OrdinalIgnoreCase);

        return rendered;
    }
}
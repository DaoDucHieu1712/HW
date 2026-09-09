using HW.Application.Abstractions.AI;

namespace HW.Application.Agents.Workflow;

public interface IWorkflowCatalog
{
    IReadOnlyList<WorkflowDefinition> All { get; }

    WorkflowDefinition Get(string name);
}

public sealed class WorkflowCatalog : IWorkflowCatalog
{
    private readonly IReadOnlyDictionary<string, WorkflowDefinition> _workflows;

    public WorkflowCatalog(IEnumerable<WorkflowDefinition> workflows)
        => _workflows = workflows.ToDictionary(workflow => workflow.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkflowDefinition> All => _workflows.Values.OrderBy(w => w.Name).ToList();

    public WorkflowDefinition Get(string name)
        => _workflows.TryGetValue(name, out var workflow)
            ? workflow
            : throw new KeyNotFoundException(
                $"No workflow named '{name}'. Registered workflows: {string.Join(", ", _workflows.Keys.Order())}.");

    /// <summary>
    /// The built-in workflows. Each is a shape worth reusing rather than an exhaustive list — a
    /// workflow is data, so a caller with a different sequence adds one rather than editing these.
    /// </summary>
    public static IReadOnlyList<WorkflowDefinition> Defaults() =>
    [
        DiagnoseBug(),
        TriageIncident(),
        CrossExamine(),
        ReviewChange(),
    ];

    /// <summary>
    /// The full engineering loop for a reported bug: gather evidence from two sources at once, let a
    /// debugger reason over both, then have a second model review the proposed fix before a human
    /// ever looks at it.
    /// </summary>
    private static WorkflowDefinition DiagnoseBug() => new(
        Name: "diagnose-bug",
        Description: "Trace logs and SQL in parallel, diagnose the cause, propose a fix, then review it.",
        Steps:
        [
            // The two tracers read different evidence and never need each other's, so running them
            // at once costs one round-trip's latency instead of two.
            new ParallelWorkflowStep(
                Id: "evidence",
                Prompt: """
                    A bug has been reported:

                    {{input}}

                    Find the evidence for it in what you can see. Report exactly what you observed,
                    with correlation ids, timings, and quoted text. Do not speculate about the fix.
                    """,
                Branches:
                [
                    new WorkflowBranch("logs", "log-tracer"),
                    new WorkflowBranch("sql", "sql-tracer"),
                ]),

            new AgentWorkflowStep(
                Id: "diagnosis",
                Agent: "bugfixer",
                Prompt: """
                    A bug has been reported:

                    {{input}}

                    Two tracers have already gathered evidence:

                    {{evidence}}

                    Read the source on that path, work out the cause, and propose a fix with
                    propose_patch. If the evidence does not identify a cause, say so and list what
                    is still missing instead of guessing.
                    """),

            new AgentWorkflowStep(
                Id: "review",
                Agent: "reviewer",
                Prompt: """
                    A debugger investigated this report:

                    {{input}}

                    and produced this diagnosis and fix:

                    {{diagnosis}}

                    Read the code it touched and say whether you would approve the change. Name any
                    specific problem you find.
                    """,
                AllowMutations: false),
        ]);

    /// <summary>
    /// Evidence only. For the moment something is on fire and the question is what happened, not
    /// what to change.
    /// </summary>
    private static WorkflowDefinition TriageIncident() => new(
        Name: "triage-incident",
        Description: "Read-only: trace logs and SQL in parallel and merge them into one timeline.",
        Steps:
        [
            new ParallelWorkflowStep(
                Id: "evidence",
                Prompt: """
                    Something is wrong with the running application:

                    {{input}}

                    Report what you can see about it. Quote exact text, give counts and timings, and
                    include correlation ids.
                    """,
                Branches:
                [
                    new WorkflowBranch("logs", "log-tracer"),
                    new WorkflowBranch("sql", "sql-tracer"),
                ],
                SynthesisAgent: "synthesizer"),
        ]);

    /// <summary>
    /// The same question to all three vendors at once, then reconciled.
    ///
    /// <para>
    /// Costs roughly three times a single run, so it earns its place on the questions where being
    /// wrong is expensive and one model's confident answer is not enough — where the models
    /// disagree is usually where the real difficulty is.
    /// </para>
    /// </summary>
    private static WorkflowDefinition CrossExamine() => new(
        Name: "cross-examine",
        Description: "Ask Claude, GPT, and Gemini the same question at once, then reconcile their answers.",
        Steps:
        [
            new ParallelWorkflowStep(
                Id: "answers",
                Prompt: "{{input}}",
                Branches:
                [
                    new WorkflowBranch("claude", "developer", LlmProvider.Claude),
                    new WorkflowBranch("gpt", "developer", LlmProvider.OpenAI),
                    new WorkflowBranch("gemini", "developer", LlmProvider.Gemini),
                ],
                SynthesisAgent: "synthesizer"),
        ]);

    private static WorkflowDefinition ReviewChange() => new(
        Name: "review-change",
        Description: "Two models review the same change independently, then their verdicts are reconciled.",
        Steps:
        [
            new ParallelWorkflowStep(
                Id: "reviews",
                Prompt: """
                    Review this change:

                    {{input}}

                    Read the surrounding code before judging it, and say whether you would approve.
                    """,
                Branches:
                [
                    new WorkflowBranch("claude", "reviewer", LlmProvider.Claude),
                    new WorkflowBranch("gpt", "reviewer", LlmProvider.OpenAI),
                ],
                SynthesisAgent: "synthesizer"),
        ]);
}

using System.Text;
using System.Text.Json;
using HW.Agentic.Abstractions;
using HW.Agentic.Core;
using HW.Agentic.Workflow;

namespace HW.Agentic.Tools;

/// <summary>
/// What the manager can hand work to. Read first: a manager that guesses at agent names burns a
/// turn on a delegation that was never going to resolve.
/// </summary>
public sealed class ListSpecialistsTool : AgentToolBase
{
    private readonly AgentDelegator _delegator;
    private readonly IWorkflowCatalog _workflows;

    public ListSpecialistsTool(AgentDelegator delegator, IWorkflowCatalog workflows, AgentLoopOptions options)
        : base(options)
    {
        _delegator = delegator;
        _workflows = workflows;
    }

    public override string Name => "list_specialists";

    public override string Description =>
        "List the specialists you can delegate to and the workflows you can run, with what each one " +
        "does, the tools it has, and the model it runs on. Call this first when you are not certain " +
        "who handles a request — the names here are the only valid ones.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public override Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
        => Task.FromResult(Serialize(new
        {
            specialists = _delegator.Delegatable().Select(agent => new
            {
                agent.Name,
                agent.Description,
                Provider = agent.Provider.ToString(),
                agent.Tools,
            }),
            workflows = _workflows.All.Select(workflow => new
            {
                workflow.Name,
                workflow.Description,
                steps = workflow.Steps.Count,
            }),
        }));
}

/// <summary>Hands one task to one specialist and returns its answer.</summary>
public sealed class DelegateTool : AgentToolBase
{
    private readonly AgentDelegator _delegator;

    public DelegateTool(AgentDelegator delegator, AgentLoopOptions options) : base(options)
        => _delegator = delegator;

    public override string Name => "delegate";

    public override string Description =>
        "Hand one task to one specialist and get its answer back. Write the task as if the " +
        "specialist knows nothing about this conversation — it does not: it sees only the text you " +
        "send, so include the symptom, the file, the correlation id, whatever it needs. Set " +
        "provider to run the same specialist on a different model. Use delegate_parallel instead " +
        "when the tasks do not depend on each other.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "agent": {
              "type": "string",
              "description": "Name of a specialist from list_specialists."
            },
            "task": {
              "type": "string",
              "description": "The complete, self-contained instruction for that specialist."
            },
            "provider": {
              "type": "string",
              "description": "Run it on this model vendor instead of its default.",
              "enum": ["Claude", "OpenAI", "Gemini"]
            },
            "allowMutations": {
              "type": "boolean",
              "description": "False withholds the specialist's write tools, so it can investigate but not propose a patch. Defaults to true."
            }
          },
          "required": ["agent", "task"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var record = await _delegator.RunAsync(
            RequiredString(input, "agent"),
            RequiredString(input, "task"),
            LlmProviders.Parse(OptionalString(input, "provider")),
            OptionalBool(input, "allowMutations", fallback: true),
            ct);

        return Format(record);
    }

    /// <summary>
    /// The specialist's answer, plus enough context for the manager to judge it. A budget-exhausted
    /// run in particular has to be flagged: its answer is a summary of unfinished work, and a
    /// manager told nothing would relay it as a finding.
    /// </summary>
    internal static string Format(DelegationRecord record)
    {
        var builder = new StringBuilder();

        builder.Append(record.Agent).Append(" (").Append(record.Provider).Append(", ")
            .Append(record.Iterations).Append(" iterations, ")
            .Append(record.Steps.Count).AppendLine(" tool calls)");

        if (record.Failed) builder.AppendLine("STATUS: FAILED — this specialist produced no answer.");
        if (record.BudgetExhausted) builder.AppendLine("STATUS: ran out of tool calls; its answer may rest on incomplete work.");

        builder.AppendLine();
        builder.AppendLine(record.Answer);

        return builder.ToString();
    }
}

/// <summary>
/// Runs several specialists at once.
///
/// <para>
/// This is where the multi-provider setup earns its keep two different ways: independent
/// sub-questions answered concurrently instead of one turn each, and the same question put to
/// different vendors when one model's confident answer is not enough.
/// </para>
/// </summary>
public sealed class DelegateParallelTool : AgentToolBase
{
    private readonly AgentDelegator _delegator;

    public DelegateParallelTool(AgentDelegator delegator, AgentLoopOptions options) : base(options)
        => _delegator = delegator;

    public override string Name => "delegate_parallel";

    public override string Description =>
        "Run several specialists at the same time and get all their answers. Use it whenever the " +
        "tasks do not depend on each other — tracing logs and tracing SQL, say — because running " +
        "them one per turn costs a round-trip each for no benefit. You can also send the SAME task " +
        "to the same specialist on different providers to see whether the models agree; where they " +
        "disagree is usually where the real difficulty is. A branch that fails is reported as " +
        "failed and the others still return.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "calls": {
              "type": "array",
              "description": "The specialists to run at once.",
              "items": {
                "type": "object",
                "properties": {
                  "agent": {
                    "type": "string",
                    "description": "Name of a specialist from list_specialists."
                  },
                  "task": {
                    "type": "string",
                    "description": "The complete, self-contained instruction for that specialist."
                  },
                  "provider": {
                    "type": "string",
                    "description": "Run this branch on a specific model vendor.",
                    "enum": ["Claude", "OpenAI", "Gemini"]
                  },
                  "allowMutations": {
                    "type": "boolean",
                    "description": "False withholds that branch's write tools. Defaults to true."
                  }
                },
                "required": ["agent", "task"]
              }
            }
          },
          "required": ["calls"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var calls = OptionalArray(input, "calls");

        if (calls.Count == 0)
            throw new ArgumentException("Argument 'calls' must contain at least one specialist to run.");

        var records = await Task.WhenAll(calls.Select(call => _delegator.RunAsync(
            RequiredString(call, "agent"),
            RequiredString(call, "task"),
            LlmProviders.Parse(OptionalString(call, "provider")),
            OptionalBool(call, "allowMutations", fallback: true),
            ct)));

        var builder = new StringBuilder();

        foreach (var record in records)
        {
            builder.Append("=== ").Append(record.Agent).Append(" @ ").Append(record.Provider).AppendLine(" ===");
            builder.AppendLine(DelegateTool.Format(record));
        }

        var failed = records.Count(record => record.Failed);

        if (failed > 0)
        {
            // Stated rather than left to be noticed. A manager that misses this presents the
            // surviving answers as though every specialist agreed.
            builder.Append(failed).Append(" of ").Append(records.Length)
                .AppendLine(" specialists failed. Do not present the rest as a complete picture.");
        }

        return builder.ToString();
    }
}

/// <summary>Runs a pre-defined workflow when one already matches the request.</summary>
public sealed class RunWorkflowTool : AgentToolBase
{
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowCatalog _catalog;

    public RunWorkflowTool(IWorkflowEngine engine, IWorkflowCatalog catalog, AgentLoopOptions options)
        : base(options)
    {
        _engine = engine;
        _catalog = catalog;
    }

    public override string Name => "run_workflow";

    public override string Description =>
        "Run a pre-defined sequence of specialists end to end. Prefer this over delegating one by " +
        "one when a workflow already matches the request — its steps are ordered and its prompts " +
        "are tuned. See list_specialists for what is registered.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "workflow": {
              "type": "string",
              "description": "Name of a registered workflow."
            },
            "input": {
              "type": "string",
              "description": "What the workflow is about — the bug report, the question, the change to review."
            }
          },
          "required": ["workflow", "input"],
          "additionalProperties": false
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var name = RequiredString(input, "workflow");

        // Checked here so an unknown name comes back naming the real ones, rather than as a bare
        // KeyNotFoundException the manager has to guess its way out of.
        if (!_catalog.All.Any(workflow => workflow.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return $"No workflow named '{name}'. Registered workflows: " +
                   string.Join(", ", _catalog.All.Select(workflow => workflow.Name)) + ".";
        }

        var result = await _engine.RunAsync(name, RequiredString(input, "input"), ct);

        var builder = new StringBuilder();
        builder.Append("Workflow '").Append(result.Workflow).Append("' ran ")
            .Append(result.Steps.Count).AppendLine(" steps.");

        foreach (var step in result.Steps)
        {
            builder.AppendLine();
            builder.Append("--- step: ").Append(step.StepId).Append(step.Failed ? " (FAILED)" : string.Empty).AppendLine(" ---");
            builder.AppendLine(step.Output);
        }

        return builder.ToString();
    }
}

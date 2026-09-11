using HW.Agentic.Abstractions;
using HW.Agentic.Prompts;

namespace HW.Agentic.Core;

public interface IAgentCatalog
{
    IReadOnlyList<AgentDefinition> All { get; }

    AgentDefinition Get(string name);

    bool TryGet(string name, out AgentDefinition definition);
}

/// <summary>
/// The registered agents, keyed by name. Registration is data, so adding a specialist means adding
/// a definition — not a class, a handler, and a controller action.
/// </summary>
public sealed class AgentCatalog : IAgentCatalog
{
    private readonly IReadOnlyDictionary<string, AgentDefinition> _agents;

    public AgentCatalog(IEnumerable<AgentDefinition> agents)
        => _agents = agents.ToDictionary(agent => agent.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AgentDefinition> All => _agents.Values.OrderBy(agent => agent.Name).ToList();

    public AgentDefinition Get(string name)
        => TryGet(name, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"No agent named '{name}'. Registered agents: {string.Join(", ", _agents.Keys.Order())}.");

    public bool TryGet(string name, out AgentDefinition definition)
        => _agents.TryGetValue(name, out definition!);

    /// <summary>
    /// The built-in specialists — the ones that work on code and on the running system, and are
    /// therefore useful in any application this runtime is dropped into.
    ///
    /// <para>
    /// A host's own agents are not listed here: they are registered alongside these by whatever
    /// feature owns them (<c>services.AddSingleton(MyAgent.Definition())</c>), because the catalog
    /// composes whatever definitions DI hands it. That direction matters — this project knowing
    /// about a feature's agent would make the runtime depend on the application it runs inside.
    /// </para>
    ///
    /// <para>
    /// Providers are assigned rather than uniform, which is the point of having three: the two
    /// tracers read a lot of noisy text and summarise it, which the cheaper models do well, while
    /// diagnosis and code changes go to the strongest one. Change the assignment freely — the loop
    /// does not care, and every agent works on every provider.
    /// </para>
    ///
    /// <para>
    /// Tool lists are narrow on purpose, and an empty one means no tools at all. A tracer given
    /// write tools will eventually decide to use them; a manager given read tools stops delegating.
    /// </para>
    /// </summary>
    public static IReadOnlyList<AgentDefinition> Defaults() =>
    [
        new(
            Name: "manager",
            Description: "Coordinates the specialists. Talk to this one — it decides who does the work and reports back.",
            SystemPrompt: ManagerPrompt.System,

            // Delegation only. It cannot read a log, a query, or a file itself, which is what keeps
            // it delegating rather than quietly doing half the work with the wrong tools.
            Tools: ["list_specialists", "delegate", "delegate_parallel", "run_workflow"],
            Provider: LlmProvider.Claude,
            MaxIterations: 12),

        new(
            Name: "developer",
            Description: "Explains how the codebase works and writes changes to it. Proposes patches for approval.",
            SystemPrompt: DeveloperAgentPrompts.Developer,
            Tools: ["list_files", "read_file", "search_code", "propose_patch"],
            Provider: LlmProvider.Claude,
            MaxIterations: 16),

        new(
            Name: "bugfixer",
            Description: "Diagnoses a failure from logs, SQL, and source, then proposes a fix for approval.",
            SystemPrompt: DeveloperAgentPrompts.BugFixer,
            Tools: ["trace_log", "trace_sql", "list_files", "read_file", "search_code", "propose_patch"],
            Provider: LlmProvider.Claude,
            MaxIterations: 20),

        new(
            Name: "log-tracer",
            Description: "Reconstructs what happened from the application log. Read-only.",
            SystemPrompt: DeveloperAgentPrompts.LogTracer,
            Tools: ["trace_log"],
            Provider: LlmProvider.Gemini,
            MaxIterations: 8),

        new(
            Name: "sql-tracer",
            Description: "Reports on the SQL a request actually issued: N+1s, slow commands, failures. Read-only.",
            SystemPrompt: DeveloperAgentPrompts.SqlTracer,
            Tools: ["trace_sql"],
            Provider: LlmProvider.OpenAI,
            MaxIterations: 8),

        new(
            Name: "reviewer",
            Description: "Reviews a proposed change against the surrounding code. Read-only.",
            SystemPrompt: DeveloperAgentPrompts.Reviewer,
            Tools: ["list_files", "read_file", "search_code"],
            Provider: LlmProvider.OpenAI,
            MaxIterations: 10),

        new(
            Name: "synthesizer",
            Description: "Merges several independent answers into one, naming where they disagreed.",
            SystemPrompt: DeveloperAgentPrompts.Synthesizer,

            // No tools at all. Its whole job is to reason over text it was handed; giving it tools
            // would let it go find its own evidence, which is exactly what makes a synthesis
            // untrustworthy.
            Tools: [],
            Provider: LlmProvider.Claude,
            MaxIterations: 1),
    ];
}

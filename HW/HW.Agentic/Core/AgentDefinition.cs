using HW.Agentic.Abstractions;

namespace HW.Agentic.Core;

/// <summary>
/// One agent: a role, the instructions that give it that role, the tools it may use, and the model
/// it runs on. Definitions are data rather than classes, so a new specialist is a registration
/// rather than a type — and so a workflow can send the same role to a different provider per branch.
/// </summary>
/// <param name="Name">Stable id used by workflows and the API. Lower-kebab-case by convention.</param>
/// <param name="Tools">
/// Tool names this agent may call. An empty list means none — an agent that reasons over what it
/// was handed rather than going to look. A narrow tool surface is the main lever on whether an
/// agent stays on task, so list what it needs and nothing more.
/// </param>
/// <param name="Provider">The default vendor. A run may override it; a fan-out always does.</param>
/// <param name="Model">
/// Model id for that vendor, or null for the provider's configured default. This is where a cheap
/// worker is made cheap.
/// </param>
/// <param name="MaxIterations">
/// Overrides the shared loop budget. A tracer that reads a few log pages needs less than a debugger
/// working through a call chain.
/// </param>
public sealed record AgentDefinition(
    string Name,
    string Description,
    string SystemPrompt,
    IReadOnlyList<string> Tools,
    LlmProvider Provider,
    string? Model = null,
    int? MaxIterations = null,
    bool EnableWebLookup = false);

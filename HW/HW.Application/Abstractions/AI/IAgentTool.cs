using System.Text.Json;

namespace HW.Application.Abstractions.AI;

/// <summary>
/// A capability the agent can invoke. Implementations live next to the feature they expose and
/// delegate to the existing CQRS handlers, so a tool call goes through the same validation,
/// transaction, and domain rules as the matching HTTP endpoint.
/// </summary>
public interface IAgentTool
{
    /// <summary>Snake_case, stable — the model refers to the tool by this name.</summary>
    string Name { get; }

    /// <summary>
    /// What the tool does and when to reach for it. This is prompt text, not documentation:
    /// a vague description is the usual reason an agent picks the wrong tool.
    /// </summary>
    string Description { get; }

    /// <summary>JSON Schema object describing the arguments.</summary>
    string InputSchemaJson { get; }

    /// <summary>
    /// True when the tool writes. The loop refuses these in read-only runs, so a lookup request
    /// cannot quietly rewrite the user's vocab list.
    /// </summary>
    bool IsMutating { get; }

    /// <summary>
    /// Runs the tool. Returning text the model can read beats returning a status code — it has to
    /// reason about the result. Throwing is fine: the loop turns it into an error tool result.
    /// </summary>
    Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);
}

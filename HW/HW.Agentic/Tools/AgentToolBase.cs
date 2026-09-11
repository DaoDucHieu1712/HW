using System.Text.Json.Serialization;
using System.Text.Json;
using HW.Agentic.Abstractions;
using HW.Agentic.Core;

namespace HW.Agentic.Tools;

/// <summary>
/// Argument reading and result formatting, shared by every tool.
///
/// <para>
/// The argument readers are lenient on purpose. Tool arguments are model-generated JSON: a number
/// arrives quoted, an optional field arrives as null rather than absent, a boolean arrives as the
/// string "true". Each of those costs a wasted round-trip if it is treated as an error, and one
/// branch if it is not.
/// </para>
/// </summary>
public abstract class AgentToolBase : IAgentTool
{
    /// <summary>
    /// Results go back to the model as text, so they are formatted for reading rather than for a
    /// parser: camelCase, indented, and with nulls dropped to keep empty fields out of the context.
    /// </summary>
    private static readonly JsonSerializerOptions ResultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    protected AgentToolBase(AgentLoopOptions options) => Options = options;

    protected AgentLoopOptions Options { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract string InputSchemaJson { get; }

    public virtual bool IsMutating => false;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    protected static string Serialize(object? value) => JsonSerializer.Serialize(value, ResultOptions);

    /// <summary>
    /// Reads a required string argument. Throwing here is how the loop turns a missing field into a
    /// correctable error result rather than a confusing failure further down.
    /// </summary>
    protected static string RequiredString(JsonElement input, string name)
        => OptionalString(input, name)
           ?? throw new ArgumentException($"Argument '{name}' is required and must be a non-empty string.");

    protected static string? OptionalString(JsonElement input, string name)
    {
        if (!TryGet(input, name, out var property)) return null;
        if (property.ValueKind != JsonValueKind.String) return null;

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    protected static int? OptionalInt(JsonElement input, string name)
    {
        if (!TryGet(input, name, out var property)) return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    protected static long? OptionalLong(JsonElement input, string name)
    {
        if (!TryGet(input, name, out var property)) return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    protected static bool OptionalBool(JsonElement input, string name, bool fallback = false)
    {
        if (!TryGet(input, name, out var property)) return fallback;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => fallback,
        };
    }

    protected static DateTimeOffset? OptionalDate(JsonElement input, string name)
    {
        var raw = OptionalString(input, name);

        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    protected static IReadOnlyList<JsonElement> OptionalArray(JsonElement input, string name)
    {
        if (!TryGet(input, name, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static bool TryGet(JsonElement input, string name, out JsonElement property)
    {
        property = default;

        return input.ValueKind == JsonValueKind.Object
               && input.TryGetProperty(name, out property)
               && property.ValueKind != JsonValueKind.Null;
    }
}
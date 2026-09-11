using System.Text.Json;
using System.Text.Json.Nodes;

namespace HW.Agentic.Providers;

/// <summary>
/// Tools declare one JSON Schema, but the vendors do not accept the same dialect. Gemini in
/// particular rejects a schema outright when it carries keywords it does not know — most commonly
/// <c>additionalProperties</c>, which every tool here sets. Rewriting the schema per provider keeps
/// that vendor quirk out of the tool definitions.
/// </summary>
public static class JsonSchemaSanitizer
{
    /// <summary>Keywords Gemini's subset of OpenAPI schema understands. Everything else is dropped.</summary>
    private static readonly HashSet<string> GeminiKeywords = new(StringComparer.Ordinal)
    {
        "type", "format", "description", "nullable", "enum", "items", "properties", "required",
    };

    public static JsonNode ForGemini(string schemaJson)
    {
        var node = JsonNode.Parse(schemaJson) ?? new JsonObject();

        return Prune(node) ?? new JsonObject { ["type"] = "object" };
    }

    private static JsonNode? Prune(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject source:
            {
                var pruned = new JsonObject();

                foreach (var (key, value) in source)
                {
                    if (!GeminiKeywords.Contains(key)) continue;

                    // "properties" is a bag of schema names, so its keys are user data and must not
                    // be filtered against the keyword list — only their values are schemas.
                    if (key == "properties" && value is JsonObject properties)
                    {
                        var prunedProperties = new JsonObject();

                        foreach (var (name, schema) in properties)
                            prunedProperties[name] = Prune(schema) ?? new JsonObject();

                        pruned[key] = prunedProperties;
                        continue;
                    }

                    pruned[key] = Prune(value);
                }

                return pruned;
            }

            case JsonArray array:
            {
                var pruned = new JsonArray();

                foreach (var item in array)
                    pruned.Add(Prune(item));

                return pruned;
            }

            default:
                // A scalar — copy it out of its old parent, since a JsonNode may only be attached once.
                return node is null ? null : JsonNode.Parse(node.ToJsonString());
        }
    }

    /// <summary>
    /// Reads a tool's schema as-is, for the providers that accept the full dialect. Parsing rather
    /// than pasting the string means a malformed schema fails here, at request build time, with the
    /// tool name in scope.
    /// </summary>
    public static JsonNode AsIs(string schemaJson)
        => JsonNode.Parse(schemaJson) ?? new JsonObject { ["type"] = "object" };

    public static JsonElement ToElement(JsonNode node)
        => JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
}

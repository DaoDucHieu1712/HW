using System.Text.Json;

namespace HW.UnitTests.TestSupport;

/// <summary>Tool arguments arrive as raw model JSON, so the tests write them the same way.</summary>
public static class Json
{
    public static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static JsonElement NoArgs() => Args("{}");

    public static JsonDocument Parse(string json) => JsonDocument.Parse(json);
}

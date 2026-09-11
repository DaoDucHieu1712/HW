using System.ComponentModel.DataAnnotations;

namespace HW.Agentic.Providers;

/// <summary>
/// What every provider adapter needs. Each vendor binds its own section, so one deployment can run
/// Claude on a strong model and Gemini on a cheap one without the agents knowing.
/// </summary>
public abstract class LlmProviderOptions
{
    /// <summary>
    /// Leave this out of appsettings and set the provider's environment variable instead. An empty
    /// key is not an error here — it means the provider is simply not configured, and the resolver
    /// leaves it out of the available list rather than failing at startup.
    /// </summary>
    public string? ApiKey { get; set; }

    [Required]
    public string Model { get; set; } = string.Empty;

    [Range(256, 32_000)]
    public int MaxTokens { get; set; } = 4_096;

    /// <summary>Override for a proxy or a compatible gateway. Empty means the vendor's own endpoint.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Wall clock for one round-trip. Generous: an agent turn on a reasoning model with a large tool
    /// result routinely takes longer than the 100-second default HttpClient allows.
    /// </summary>
    [Range(10, 900)]
    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class OpenAiAgentOptions : LlmProviderOptions
{
    public OpenAiAgentOptions() => Model = "gpt-5";

    /// <summary>
    /// Reasoning depth for the o-series and GPT-5 family: minimal, low, medium, or high. Left unset
    /// for models that do not take it — sending it to one that does not is a 400.
    /// </summary>
    public string? ReasoningEffort { get; set; } = "medium";
}

public sealed class GeminiAgentOptions : LlmProviderOptions
{
    public GeminiAgentOptions() => Model = "gemini-2.5-pro";
}

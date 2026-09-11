using System.ComponentModel.DataAnnotations;

namespace HW.Agentic.Providers;

/// <summary>Bound from the <c>Anthropic</c> configuration section.</summary>
public sealed class AnthropicAgentOptions
{
    /// <summary>
    /// Leave this out of appsettings.json and set <c>ANTHROPIC_API_KEY</c> instead — the SDK picks
    /// that up on its own, and a key committed to source control is a key that has to be rotated.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model id. Opus 5 by default: the loop's quality lives or dies on tool selection, and
    /// picking the wrong tool costs a whole extra round-trip.
    /// </summary>
    [Required]
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>
    /// Output cap per round-trip. A vocab answer is short; the ceiling exists to stop a runaway
    /// turn, not to size the reply. Well under the SDK's HTTP timeout, so no streaming is needed.
    /// </summary>
    [Range(256, 32_000)]
    public int MaxTokens { get; set; } = 4_096;

    /// <summary>
    /// How hard the model thinks: low, medium, high, or max. High is the default and is the right
    /// trade for this workload — lookups and short review sessions do not repay more.
    /// </summary>
    public string Effort { get; set; } = "high";

    /// <summary>
    /// Whether to return a readable summary of the model's reasoning. Off by default: the loop's
    /// trace already shows what it did, and the summary is billed context nobody reads.
    /// </summary>
    public bool ShowThinking { get; set; }
}

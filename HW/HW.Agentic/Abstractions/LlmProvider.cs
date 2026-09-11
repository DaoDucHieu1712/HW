namespace HW.Agentic.Abstractions;

/// <summary>
/// The model vendors the agents can run on. Each has its own adapter behind
/// <see cref="IAgentChatClient"/>; everything above that seam is provider-neutral.
/// </summary>
public enum LlmProvider
{
    Claude = 0,
    OpenAI = 1,
    Gemini = 2
}

public static class LlmProviders
{
    /// <summary>
    /// Parses a provider name off the wire — a tool argument the model wrote, or a field on a
    /// request body. Its whole job is the error message: an unrecognised name should say what the
    /// valid ones are, because the caller is usually a model that can correct itself when told.
    /// </summary>
    /// <returns>Null when nothing was supplied, which means "use the agent's own provider".</returns>
    public static LlmProvider? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (Enum.TryParse<LlmProvider>(value, ignoreCase: true, out var provider))
            return provider;

        throw new ArgumentException(
            $"Unknown provider '{value}'. Valid providers: {string.Join(", ", Enum.GetNames<LlmProvider>())}.");
    }
}

using HW.Application.Abstractions.AI;

namespace HW.Infrastructure.AI;

/// <summary>
/// Resolves an adapter by provider. Only providers with a configured key are registered, so
/// <see cref="Available"/> is the honest list of what this deployment can actually call — a
/// workflow can check it before fanning out rather than discovering the gap in a failed branch.
/// </summary>
public sealed class AgentChatClientResolver : IAgentChatClientResolver
{
    private readonly IReadOnlyDictionary<LlmProvider, IAgentChatClient> _clients;

    public AgentChatClientResolver(IEnumerable<IAgentChatClient> clients)
    {
        // Last registration wins, which is what makes a deployment able to swap one adapter without
        // unregistering the other.
        _clients = clients.GroupBy(client => client.Provider)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    public IReadOnlyList<LlmProvider> Available => _clients.Keys.OrderBy(provider => provider).ToList();

    public bool IsAvailable(LlmProvider provider) => _clients.ContainsKey(provider);

    public IAgentChatClient Resolve(LlmProvider provider)
        => _clients.TryGetValue(provider, out var client)
            ? client
            : throw new InvalidOperationException(
                $"No adapter is registered for {provider}. Configure its API key " +
                $"({ApiKeyHint(provider)}) to enable it. Available now: " +
                (Available.Count == 0 ? "none" : string.Join(", ", Available)) + ".");

    private static string ApiKeyHint(LlmProvider provider) => provider switch
    {
        LlmProvider.Claude => "ANTHROPIC_API_KEY or Anthropic:ApiKey",
        LlmProvider.OpenAI => "OPENAI_API_KEY or OpenAI:ApiKey",
        LlmProvider.Gemini => "GEMINI_API_KEY or Gemini:ApiKey",
        _ => "its API key",
    };
}

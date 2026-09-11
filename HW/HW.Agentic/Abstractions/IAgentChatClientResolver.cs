namespace HW.Agentic.Abstractions;

/// <summary>
/// Picks the adapter for a provider. Agents name the provider they want, and a workflow can fan the
/// same task out across several — so the choice is made per run, not at registration.
/// </summary>
public interface IAgentChatClientResolver
{
    /// <summary>Providers that are actually configured. An unconfigured one is not listed.</summary>
    IReadOnlyList<LlmProvider> Available { get; }

    bool IsAvailable(LlmProvider provider);

    /// <exception cref="InvalidOperationException">
    /// The provider has no adapter registered — almost always a missing API key rather than a bug,
    /// so the message says which provider and what to configure.
    /// </exception>
    IAgentChatClient Resolve(LlmProvider provider);
}

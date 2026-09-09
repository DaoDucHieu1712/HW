namespace HW.Application.Abstractions.AI;

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

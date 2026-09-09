namespace HW.Application.Abstractions.Chat;

/// <param name="Role">"user" or "assistant".</param>
public sealed record ChatMessage(string Role, string Text, DateTimeOffset At);

public sealed record Conversation(
    string Id,
    string Agent,
    IReadOnlyList<ChatMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Server-side memory for the chat endpoint, so a client sends a conversation id and its next
/// message rather than replaying the whole exchange each time.
///
/// <para>
/// The transcript is text only. Within one run the loop replays the provider's own blocks, but
/// across calls only what was said is kept — which is also what lets a conversation carry on after
/// its agent has been pointed at a different provider.
/// </para>
/// </summary>
public interface IConversationStore
{
    Conversation Start(string agent);

    Conversation? Get(string id);

    /// <exception cref="KeyNotFoundException">The conversation has expired or never existed.</exception>
    Conversation Append(string id, ChatMessage message);

    bool Delete(string id);

    IReadOnlyList<Conversation> List(int limit);
}

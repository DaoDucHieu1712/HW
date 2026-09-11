using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using HW.Agentic.Abstractions.Chat;

namespace HW.Infrastructure.Chat;

public sealed class ChatOptions
{
    /// <summary>Agent the chat endpoint talks to when the caller does not name one.</summary>
    [Required]
    public string DefaultAgent { get; set; } = "manager";

    /// <summary>
    /// Turns kept per conversation. Every turn is re-sent on every call, so this is a direct cost
    /// and context-window control, not just a memory bound — the oldest turns fall off the front.
    /// </summary>
    [Range(2, 200)]
    public int MaxTurns { get; set; } = 30;

    [Range(1, 10_000)]
    public int MaxConversations { get; set; } = 500;

    /// <summary>Idle lifetime. An abandoned conversation is not worth the memory it holds.</summary>
    [Range(1, 1_440)]
    public int IdleMinutes { get; set; } = 120;
}

/// <summary>
/// Conversations held in process memory.
///
/// <para>
/// Not durable, and deliberately so: this is chat scrollback, and the cost of losing it on a restart
/// is that the user repeats their question. Making it durable would mean a table, a migration, and a
/// retention policy for transcripts that may quote application logs — a much larger decision than
/// the feature warrants. Anything that must survive a restart belongs in a real store, and the
/// interface is where that would be swapped in.
/// </para>
///
/// <para>
/// Bounded three ways — turns per conversation, conversations per process, and idle lifetime —
/// because an unbounded cache of transcripts is a memory leak wearing a feature's clothes.
/// </para>
/// </summary>
public sealed class ConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);
    private readonly ChatOptions _options;

    public ConversationStore(ChatOptions options) => _options = options;

    public Conversation Start(string agent)
    {
        Evict();

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid().ToString("N")[..16], agent, [], now, now);

        _conversations[conversation.Id] = conversation;
        return conversation;
    }

    public Conversation? Get(string id)
    {
        if (!_conversations.TryGetValue(id, out var conversation)) return null;

        // Expiry is checked on read rather than swept on a timer: a conversation nobody asks for
        // costs nothing until the next eviction, and this keeps a background task out of the design.
        if (IsExpired(conversation))
        {
            _conversations.TryRemove(id, out _);
            return null;
        }

        return conversation;
    }

    public Conversation Append(string id, ChatMessage message)
    {
        var conversation = Get(id)
            ?? throw new KeyNotFoundException(
                $"Conversation '{id}' does not exist or has expired after {_options.IdleMinutes} minutes of inactivity. " +
                "Start a new one by omitting the conversation id.");

        var messages = conversation.Messages.Append(message).ToList();

        // Trim from the front. Losing the oldest turns degrades the conversation; letting it grow
        // without limit degrades every request in it.
        if (messages.Count > _options.MaxTurns)
            messages = messages.Skip(messages.Count - _options.MaxTurns).ToList();

        var updated = conversation with { Messages = messages, UpdatedAt = DateTimeOffset.UtcNow };

        _conversations[id] = updated;
        return updated;
    }

    public bool Delete(string id) => _conversations.TryRemove(id, out _);

    public IReadOnlyList<Conversation> List(int limit)
        => _conversations.Values
            .Where(conversation => !IsExpired(conversation))
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .Take(Math.Max(limit, 1))
            .ToList();

    private bool IsExpired(Conversation conversation)
        => conversation.UpdatedAt.AddMinutes(_options.IdleMinutes) < DateTimeOffset.UtcNow;

    /// <summary>
    /// Drops expired conversations, then the least recently used if the process is still over its
    /// ceiling. Runs only when a new conversation starts, which is the one moment the count grows.
    /// </summary>
    private void Evict()
    {
        foreach (var (id, conversation) in _conversations)
        {
            if (IsExpired(conversation)) _conversations.TryRemove(id, out _);
        }

        var excess = _conversations.Count - _options.MaxConversations + 1;
        if (excess <= 0) return;

        foreach (var conversation in _conversations.Values.OrderBy(c => c.UpdatedAt).Take(excess))
            _conversations.TryRemove(conversation.Id, out _);
    }
}

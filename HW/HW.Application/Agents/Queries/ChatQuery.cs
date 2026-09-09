using FluentValidation;
using HW.Application.Abstractions.AI;
using HW.Application.Abstractions.Chat;
using HW.Application.Agents.Dtos;
using HW.Application.CQRS;

namespace HW.Application.Agents.Queries;

/// <summary>
/// One turn of the chat endpoint: the user's message goes to an agent — the manager by default —
/// and the answer comes back with everything the manager delegated to produce it.
/// </summary>
public record ChatQuery(AgentDtos.ChatRequestDto Request) : IQuery<AgentDtos.ChatResponseDto>;

public class ChatQueryValidator : AbstractValidator<ChatQuery>
{
    public ChatQueryValidator()
    {
        RuleFor(x => x.Request).Cascade(CascadeMode.Stop).NotNull();
        RuleFor(x => x.Request.Message).NotEmpty().MaximumLength(20_000).When(x => x.Request is not null);
    }
}

public class ChatQueryHandler : IQueryHandler<ChatQuery, AgentDtos.ChatResponseDto>
{
    private readonly IEngineerLoop _loop;
    private readonly IAgentCatalog _catalog;
    private readonly IConversationStore _conversations;
    private readonly IDelegationLedger _ledger;
    private readonly string _defaultAgent;

    public ChatQueryHandler(
        IEngineerLoop loop,
        IAgentCatalog catalog,
        IConversationStore conversations,
        IDelegationLedger ledger,
        ChatDefaults defaults)
    {
        _loop = loop;
        _catalog = catalog;
        _conversations = conversations;
        _ledger = ledger;
        _defaultAgent = defaults.Agent;
    }

    public async Task<AgentDtos.ChatResponseDto> Handle(ChatQuery request, CancellationToken ct)
    {
        var dto = request.Request;

        var conversation = dto.ConversationId is null
            ? _conversations.Start(dto.Agent ?? _defaultAgent)
            : _conversations.Get(dto.ConversationId)
              ?? throw new KeyNotFoundException(
                  $"Conversation '{dto.ConversationId}' does not exist or has expired. " +
                  "Omit conversationId to start a new one.");

        // The conversation remembers which agent it belongs to, so a follow-up cannot silently
        // land on a different one and inherit a transcript written for its predecessor.
        var agent = _catalog.Get(dto.Agent ?? conversation.Agent);

        var history = conversation.Messages
            .Select(message => new AgentConversationTurn(message.Role, message.Text))
            .ToList();

        var result = await _loop.RunAsync(
            new AgentRunRequest(
                agent,
                dto.Message,
                history,
                dto.AllowMutations,
                AgentDtos.ParseProvider(dto.Provider)),
            ct);

        var now = DateTimeOffset.UtcNow;
        _conversations.Append(conversation.Id, new ChatMessage("user", dto.Message, now));
        _conversations.Append(conversation.Id, new ChatMessage("assistant", result.Answer, DateTimeOffset.UtcNow));

        var delegations = _ledger.Entries
            .Select(entry => new AgentDtos.DelegationSummaryDto(
                entry.Agent,
                entry.Provider.ToString(),
                entry.Task,
                entry.Answer,
                entry.Failed,
                entry.Error,
                entry.Iterations,
                entry.BudgetExhausted,
                entry.Steps,
                entry.Usage,
                entry.ElapsedMs))
            .ToList();

        return new AgentDtos.ChatResponseDto(
            conversation.Id,
            result.Agent,
            result.Provider.ToString(),
            result.Answer,
            result.Iterations,
            result.BudgetExhausted,
            result.StopReason,
            delegations,
            result.Steps,

            // The manager's own tokens plus every specialist's — what the turn actually cost. The
            // manager's share alone would understate a delegated answer by most of its price.
            _ledger.Entries.Aggregate(result.Usage, (total, entry) => total + entry.Usage),
            result.ElapsedMs);
    }
}

/// <summary>
/// Which agent the chat endpoint talks to when the caller does not name one. A tiny type rather
/// than a bare string so it can be injected without an options package reference in this layer.
/// </summary>
public sealed class ChatDefaults
{
    public string Agent { get; init; } = "manager";
}

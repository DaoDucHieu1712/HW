using FluentValidation;
using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Agent;
using HW.Application.Features.Vocabs.Dtos;

namespace HW.Application.Features.Vocabs.Queries.AskVocabAgent;

/// <summary>
/// One question put to the vocab agent.
///
/// <para>
/// Deliberately an <see cref="IQuery{TResponse}"/> and not a command, even though the agent may
/// write: <c>TransactionBehavior</c> opens a transaction around a command and holds it for the whole
/// handler, which for an agent run means holding it across every model round-trip — potentially
/// minutes. Each tool call already runs its own command through the sender and commits on its own,
/// which is also what makes the audit trail in the response honest about what was written.
/// </para>
/// </summary>
public record AskVocabAgentQuery(VocabAgentDtos.AskVocabAgentRequestDto Request)
    : IQuery<VocabAgentDtos.VocabAgentResponseDto>;

public class AskVocabAgentQueryValidator : AbstractValidator<AskVocabAgentQuery>
{
    public AskVocabAgentQueryValidator()
    {
        // Stop on the first failure: without this, a request body that deserialized to null would
        // still be walked into by the Question rule.
        RuleFor(x => x.Request).Cascade(CascadeMode.Stop).NotNull();
        RuleFor(x => x.Request.Question).NotEmpty().MaximumLength(4_000).When(x => x.Request is not null);
    }
}

public class AskVocabAgentQueryHandler
    : IQueryHandler<AskVocabAgentQuery, VocabAgentDtos.VocabAgentResponseDto>
{
    private readonly IVocabAgent _agent;

    public AskVocabAgentQueryHandler(IVocabAgent agent) => _agent = agent;

    public Task<VocabAgentDtos.VocabAgentResponseDto> Handle(AskVocabAgentQuery request, CancellationToken ct)
        => _agent.RunAsync(request.Request, ct);
}

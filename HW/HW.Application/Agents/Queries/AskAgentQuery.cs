using FluentValidation;
using HW.Agentic.Core;
using HW.Application.Agents.Dtos;
using HW.Agentic.Workflow;
using HW.Application.CQRS;

namespace HW.Application.Agents.Queries;

/// <summary>
/// Runs one agent against one task.
///
/// <para>
/// A query rather than a command even though the agent may propose patches: <c>TransactionBehavior</c>
/// wraps a command in a database transaction and holds it for the whole handler, which for an agent
/// run means holding it across every model round-trip — minutes, in a fan-out. Nothing this handler
/// does needs that transaction; the tools that write open their own.
/// </para>
/// </summary>
public record AskAgentQuery(AgentDtos.AskAgentRequestDto Request) : IQuery<AgentRunResult>;

public class AskAgentQueryValidator : AbstractValidator<AskAgentQuery>
{
    public AskAgentQueryValidator()
    {
        RuleFor(x => x.Request).Cascade(CascadeMode.Stop).NotNull();
        RuleFor(x => x.Request.Agent).NotEmpty().When(x => x.Request is not null);
        RuleFor(x => x.Request.Task).NotEmpty().MaximumLength(20_000).When(x => x.Request is not null);
    }
}

public class AskAgentQueryHandler : IQueryHandler<AskAgentQuery, AgentRunResult>
{
    private readonly IEngineerLoop _loop;
    private readonly IAgentCatalog _catalog;

    public AskAgentQueryHandler(IEngineerLoop loop, IAgentCatalog catalog)
    {
        _loop = loop;
        _catalog = catalog;
    }

    public Task<AgentRunResult> Handle(AskAgentQuery request, CancellationToken ct)
    {
        var dto = request.Request;

        var history = dto.History?
            .Select(turn => new AgentConversationTurn(turn.Role, turn.Text))
            .ToList();

        return _loop.RunAsync(
            new AgentRunRequest(
                _catalog.Get(dto.Agent),
                dto.Task,
                history,
                dto.AllowMutations,
                AgentDtos.ParseProvider(dto.Provider)),
            ct);
    }
}

public record RunWorkflowQuery(string Workflow, string Input) : IQuery<WorkflowRunResult>;

public class RunWorkflowQueryValidator : AbstractValidator<RunWorkflowQuery>
{
    public RunWorkflowQueryValidator()
    {
        RuleFor(x => x.Workflow).NotEmpty();
        RuleFor(x => x.Input).NotEmpty().MaximumLength(20_000);
    }
}

public class RunWorkflowQueryHandler : IQueryHandler<RunWorkflowQuery, WorkflowRunResult>
{
    private readonly IWorkflowEngine _engine;

    public RunWorkflowQueryHandler(IWorkflowEngine engine) => _engine = engine;

    public Task<WorkflowRunResult> Handle(RunWorkflowQuery request, CancellationToken ct)
        => _engine.RunAsync(request.Workflow, request.Input, ct);
}

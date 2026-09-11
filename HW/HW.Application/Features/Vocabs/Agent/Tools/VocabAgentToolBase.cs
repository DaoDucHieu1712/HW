using HW.Agentic.Core;
using HW.Agentic.Tools;
using MediatR;

namespace HW.Application.Features.Vocabs.Agent.Tools;

/// <summary>
/// Base for the vocabulary tools.
///
/// <para>
/// Every one of them goes through <see cref="ISender"/> rather than touching a repository, so a tool
/// call runs the same handler, the same FluentValidation rules, and the same transaction behavior as
/// the matching HTTP endpoint. The agent gets no privileged path into the data.
/// </para>
/// </summary>
public abstract class VocabAgentToolBase : AgentToolBase
{
    protected VocabAgentToolBase(ISender sender, AgentLoopOptions options) : base(options)
        => Sender = sender;

    protected ISender Sender { get; }
}

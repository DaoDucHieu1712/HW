using HW.Api.Models;
using HW.Agentic.Abstractions.Chat;
using HW.Application.Agents.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Agents.Dtos.AgentDtos;

namespace HW.Api.Controllers;

/// <summary>
/// Ask the team something in plain language.
///
/// <para>
/// This is the front door. Messages go to the manager, which decides which specialists to put on the
/// question — reading logs, reading the SQL that ran, reading the code, or several at once across
/// different model vendors — and answers from what they report. The other endpoints under
/// <c>/api/dev-agent</c> are for driving one specialist or one workflow directly, when you already
/// know which you want.
/// </para>
/// </summary>
[Route("api/chat")]
[ApiController]
public class ChatController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IConversationStore _conversations;

    public ChatController(ISender sender, IConversationStore conversations)
    {
        _sender = sender;
        _conversations = conversations;
    }

    /// <summary>Send a message and get the team's answer.</summary>
    /// <remarks>
    /// Omit <c>conversationId</c> to start a conversation; send back the one in the response to
    /// continue it — the history is kept server-side, so there is no need to replay it.
    ///
    /// <para>
    /// The response carries the answer plus <c>delegations</c>: which specialist ran, on which
    /// vendor, what it was asked, what it found, and every tool call it made. That is what makes the
    /// answer checkable rather than something to take on trust. <c>usage</c> is the whole turn's
    /// token cost, manager and specialists together.
    /// </para>
    ///
    /// <para>
    /// Set <c>allowMutations</c> to false to keep the run read-only: no specialist can propose a
    /// patch, and the write tools are never even offered to them.
    /// </para>
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] ChatRequestDto dto, CancellationToken ct)
    {
        var result = await _sender.Send(new ChatQuery(dto), ct);
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>The transcript of one conversation.</summary>
    [HttpGet("{conversationId}")]
    public IActionResult GetConversation(string conversationId)
    {
        var conversation = _conversations.Get(conversationId);

        return conversation is null
            ? NotFound(ApiResponseFactory.NotFound<object>(
                $"No conversation '{conversationId}'. It may have expired — start a new one by omitting the id."))
            : Ok(ApiResponseFactory.Success(conversation));
    }

    /// <summary>Recent conversations, most recently used first.</summary>
    /// <remarks>Held in memory only: this list does not survive a restart of the application.</remarks>
    [HttpGet]
    public IActionResult GetConversations([FromQuery] int limit = 20)
        => Ok(ApiResponseFactory.Success(_conversations.List(limit)));

    [HttpDelete("{conversationId}")]
    public IActionResult DeleteConversation(string conversationId)
        => _conversations.Delete(conversationId)
            ? NoContent()
            : NotFound(ApiResponseFactory.NotFound<object>($"No conversation '{conversationId}'."));
}

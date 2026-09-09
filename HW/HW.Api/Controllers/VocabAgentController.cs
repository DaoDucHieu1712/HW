using HW.Api.Models;
using HW.Application.Features.Vocabs.Queries.AskVocabAgent;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.Vocabs.Dtos.VocabAgentDtos;

namespace HW.Api.Controllers;

/// <summary>
/// The conversational front end to the vocabulary features. Where <c>VocabController</c> exposes
/// one operation per endpoint, this takes a question and lets the agent decide which of those
/// operations to run, and in what order.
/// </summary>
[Route("api/vocab/agent")]
[ApiController]
public class VocabAgentController : ControllerBase
{
    private readonly ISender _sender;

    public VocabAgentController(ISender sender) => _sender = sender;

    /// <summary>
    /// Ask the vocab agent something — look a word up, save it, ask what is due today, or run a
    /// review session. The agent works through the same vocab operations this API exposes, and the
    /// response carries the tool calls it made so its work can be checked.
    /// </summary>
    /// <remarks>
    /// Deliberately not cached: the same question can legitimately produce a different answer, and
    /// the agent may write. Send prior turns back in <c>history</c> to continue a conversation —
    /// nothing is stored between calls.
    /// </remarks>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskVocabAgentRequestDto dto, CancellationToken ct)
    {
        var result = await _sender.Send(new AskVocabAgentQuery(dto), ct);
        return Ok(ApiResponseFactory.Success(result));
    }
}

using HW.Api.Models;
using HW.Application.Features.Vocabs.Commands.CreateVocab;
using HW.Application.Features.Vocabs.Commands.DeleteVocab;
using HW.Application.Features.Vocabs.Commands.ReviewVocab;
using HW.Application.Features.Vocabs.Commands.UpdateVocab;
using HW.Application.Features.Vocabs.Queries.GetDailyMission;
using HW.Application.Features.Vocabs.Queries.GetVocabById;
using HW.Application.Features.Vocabs.Queries.GetVocabs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.Vocabs.Dtos.VocabDtos;

namespace HW.Api.Controllers;

[Route("api/vocab")]
[ApiController]
public class VocabController : ControllerBase
{
    private readonly ISender _sender;

    public VocabController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] VocabPagingRequestDto request)
    {
        var result = await _sender.Send(new GetVocabsQuery(request.Search, request.PageIndex, request.PageSize));
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>Returns all vocab words due for review today (daily mission).</summary>
    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyMission()
    {
        var result = await _sender.Send(new GetDailyMissionQuery());
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var result = await _sender.Send(new GetVocabByIdQuery(id));
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVocabRequestDto dto)
    {
        await _sender.Send(new CreateVocabCommand(dto.Word, dto.Meaning, dto.Example, dto.Note));
        return NoContent();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateVocabRequestDto dto)
    {
        await _sender.Send(new UpdateVocabCommand(id, dto.Word, dto.Meaning, dto.Example, dto.Note));
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _sender.Send(new DeleteVocabCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Marks a vocab word as reviewed, advancing it to the next review stage.
    /// Stages: 0 (new) → 1 (day 3) → 2 (day 7) → 3 (day 14, completed).
    /// </summary>
    [HttpPost("{id}/review")]
    public async Task<IActionResult> Review(string id)
    {
        await _sender.Send(new ReviewVocabCommand(id));
        return NoContent();
    }
}

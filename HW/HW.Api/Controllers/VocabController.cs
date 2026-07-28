using HW.Api.Caching;
using HW.Api.Models;
using HW.Application.Features.Vocabs.Commands.CreateVocab;
using HW.Application.Features.Vocabs.Commands.DeleteVocab;
using HW.Application.Features.Vocabs.Commands.ReviewVocab;
using HW.Application.Features.Vocabs.Commands.UpdateVocab;
using HW.Application.Features.Vocabs.Commands.SubmitFlashCardSession;
using HW.Application.Features.Vocabs.Queries.GenerateVocabExam;
using HW.Application.Features.Vocabs.Queries.GetDailyMission;
using HW.Application.Features.Vocabs.Queries.GetFlashCards;
using HW.Application.Features.Vocabs.Queries.GetVocabById;
using HW.Application.Features.Vocabs.Queries.GetVocabs;
using HW.Application.Features.Vocabs.Queries.GradeVocabExam;
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
    [Cache(60)]
    public async Task<IActionResult> GetAll([FromQuery] VocabPagingRequestDto request)
    {
        var result = await _sender.Send(new GetVocabsQuery(request.Search, request.FromDate, request.ToDate, request.PageIndex, request.PageSize));
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>Returns all vocab words due for review today (daily mission).</summary>
    [HttpGet("daily")]
    [Cache(300)]
    public async Task<IActionResult> GetDailyMission()
    {
        var result = await _sender.Send(new GetDailyMissionQuery());
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpGet("{id}")]
    [Cache(120)]
    public async Task<IActionResult> GetById(string id)
    {
        var result = await _sender.Send(new GetVocabByIdQuery(id));
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpPost]
    [InvalidateCache("vocab")]
    public async Task<IActionResult> Create([FromBody] CreateVocabRequestDto dto)
    {
        await _sender.Send(new CreateVocabCommand(dto.Word, dto.Content));
        return NoContent();
    }

    [HttpPut("{id}")]
    [InvalidateCache("vocab")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateVocabRequestDto dto)
    {
        await _sender.Send(new UpdateVocabCommand(id, dto.Word, dto.Content));
        return NoContent();
    }

    [HttpDelete("{id}")]
    [InvalidateCache("vocab")]
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
    [InvalidateCache("vocab")]
    public async Task<IActionResult> Review(string id)
    {
        await _sender.Send(new ReviewVocabCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Returns a shuffled deck of flashcards. Set UseDaily=true to only include words due for review.
    /// Optionally filter by WordType (0-6) or ReviewStage (0-3) and limit with Count.
    /// </summary>
    [HttpGet("flashcards")]
    public async Task<IActionResult> GetFlashCards([FromQuery] GetFlashCardsRequestDto request)
    {
        var result = await _sender.Send(new GetFlashCardsQuery(request.Count, request.ReviewStage, request.UseDaily));
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>
    /// Submits flashcard session results. Cards marked Knew=true advance one review stage.
    /// Returns a session summary with total, knew, and didn't-know counts.
    /// </summary>
    [HttpPost("flashcards/submit")]
    [InvalidateCache("vocab")]
    public async Task<IActionResult> SubmitFlashCards([FromBody] SubmitFlashCardSessionRequestDto dto)
    {
        var result = await _sender.Send(new SubmitFlashCardSessionCommand(dto.Results));
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>
    /// Generates a set of exam questions from your vocab list (no DB storage).
    /// QuestionTypes: 0 = MultipleChoice, 1 = TrueFalse, 2 = Written. Omit to mix all.
    /// </summary>
    [HttpPost("exam/generate")]
    public async Task<IActionResult> GenerateExam([FromBody] GenerateVocabExamRequestDto dto)
    {
        var result = await _sender.Send(new GenerateVocabExamQuery(dto.QuestionCount, dto.From, dto.To));
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>
    /// Grades answers against the correct vocab meanings and returns score + per-question results.
    /// </summary>
    [HttpPost("exam/grade")]
    public async Task<IActionResult> GradeExam([FromBody] GradeVocabExamRequestDto dto)
    {
        var result = await _sender.Send(new GradeVocabExamQuery(dto.Answers));
        return Ok(ApiResponseFactory.Success(result));
    }
}

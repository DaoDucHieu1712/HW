using HW.Api.Models;
using HW.Application.Features.Notes.Commands.CreateNote;
using HW.Application.Features.Notes.Commands.DeleteNote;
using HW.Application.Features.Notes.Commands.MoveNote;
using HW.Application.Features.Notes.Commands.RestoreNote;
using HW.Application.Features.Notes.Commands.UpdateNote;
using HW.Application.Features.Notes.Queries.GetNoteById;
using HW.Application.Features.Notes.Queries.GetNotes;
using HW.Application.Features.Notes.Queries.GetTrashedItems;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.Notes.Dtos.NoteDtos;

namespace HW.Api.Controllers;

[Route("api/note")]
[ApiController]
public class NoteController : ControllerBase
{
    private readonly ISender _sender;

    public NoteController(ISender sender) => _sender = sender;

    /// <summary>Lists notes in a folder. Omit folderId to list root-level notes.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? folderId, [FromQuery] string? search,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _sender.Send(new GetNotesQuery(folderId, search, pageIndex, pageSize));
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>Returns all folders and notes currently in Trash.</summary>
    [HttpGet("trash")]
    public async Task<IActionResult> GetTrashed()
    {
        var result = await _sender.Send(new GetTrashedItemsQuery());
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var result = await _sender.Send(new GetNoteByIdQuery(id));
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNoteRequestDto dto)
    {
        await _sender.Send(new CreateNoteCommand(dto.Title, dto.Content, dto.FolderId));
        return NoContent();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateNoteRequestDto dto)
    {
        await _sender.Send(new UpdateNoteCommand(id, dto.Title, dto.Content));
        return NoContent();
    }

    /// <summary>Moves the note to Trash.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _sender.Send(new DeleteNoteCommand(id));
        return NoContent();
    }

    /// <summary>Moves the note to a different folder. Set FolderId to null to move to root.</summary>
    [HttpPatch("{id}/move")]
    public async Task<IActionResult> Move(string id, [FromBody] MoveNoteRequestDto dto)
    {
        await _sender.Send(new MoveNoteCommand(id, dto.FolderId));
        return NoContent();
    }

    /// <summary>Restores a note from Trash. If its folder is also in Trash, the note is restored to root.</summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        await _sender.Send(new RestoreNoteCommand(id));
        return NoContent();
    }
}

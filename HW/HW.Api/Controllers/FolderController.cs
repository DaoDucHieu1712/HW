using HW.Api.Models;
using HW.Application.Features.Folders.Commands.CreateFolder;
using HW.Application.Features.Folders.Commands.DeleteFolder;
using HW.Application.Features.Folders.Commands.MoveFolder;
using HW.Application.Features.Folders.Commands.RestoreFolder;
using HW.Application.Features.Folders.Commands.UpdateFolder;
using HW.Application.Features.Folders.Queries.GetFolderById;
using HW.Application.Features.Folders.Queries.GetFolders;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.Folders.Dtos.FolderDtos;

namespace HW.Api.Controllers;

[Route("api/folder")]
[ApiController]
public class FolderController : ControllerBase
{
    private readonly ISender _sender;

    public FolderController(ISender sender) => _sender = sender;

    /// <summary>Returns the full folder tree (nested).</summary>
    [HttpGet]
    public async Task<IActionResult> GetTree()
    {
        var result = await _sender.Send(new GetFoldersQuery());
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var result = await _sender.Send(new GetFolderByIdQuery(id));
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequestDto dto)
    {
        await _sender.Send(new CreateFolderCommand(dto.Name, dto.ParentId, dto.Icon));
        return NoContent();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateFolderRequestDto dto)
    {
        await _sender.Send(new UpdateFolderCommand(id, dto.Name, dto.Icon));
        return NoContent();
    }

    /// <summary>Moves a folder and its entire subtree to Trash.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _sender.Send(new DeleteFolderCommand(id));
        return NoContent();
    }

    /// <summary>Changes the folder's parent. Set NewParentId to null to move to root.</summary>
    [HttpPatch("{id}/move")]
    public async Task<IActionResult> Move(string id, [FromBody] MoveFolderRequestDto dto)
    {
        await _sender.Send(new MoveFolderCommand(id, dto.NewParentId));
        return NoContent();
    }

    /// <summary>Restores a folder from Trash. If its parent is also in Trash, the folder is restored to root.</summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        await _sender.Send(new RestoreFolderCommand(id));
        return NoContent();
    }
}

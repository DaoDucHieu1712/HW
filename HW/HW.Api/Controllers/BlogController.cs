using HW.Api.Models;
using HW.Application.Features.Blogs.Commands.CreateBlog;
using HW.Application.Features.Blogs.Commands.DeleteBlog;
using HW.Application.Features.Blogs.Commands.UpdateBlog;
using HW.Application.Features.Blogs.Queries.GetBlogById;
using HW.Application.Features.Blogs.Queries.GetBlogs;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.Blogs.Dtos.BlogDtos;

namespace HW.Api.Controllers;

[Route("api/blog")]
[ApiController]
public class BlogController : ControllerBase
{
    private readonly ISender _sender;

    public BlogController(ISender sender)
        => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> GetAllAndPaging([FromQuery] BlogPagingAndFilterResponseDto blogDto)
    {
        var rs = await _sender.Send(new GetBlogsQuery(blogDto.Search, blogDto.PageIndex, blogDto.PageSize));
        return Ok(ApiResponseFactory.Success(rs));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var rs = await _sender.Send(new GetBlogByIdQuery(id));
        return Ok(ApiResponseFactory.Success(rs));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateBlogRequestDto dto)
    {
        await _sender.Send(new CreateBlogCommand(dto.Title, dto.Content));
        return NoContent();
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateBlogRequestDto dto)
    {
        await _sender.Send(new UpdateBlogCommand(dto.Id, dto.Title, dto.Content));
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _sender.Send(new DeleteBlogCommand(id));
        return NoContent();
    }
}

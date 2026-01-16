using HW.Application.Services;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Dtos.BlogDtos;

namespace HW.Api.Controllers
{
    [Route("api/blog")]
    [ApiController]
    public class BlogController : ControllerBase
    {
        private readonly IBlogService _blogService;

        public BlogController(IBlogService blogService)
        {
            _blogService = blogService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllAndPaging([FromQuery] BlogPagingAndFilterResponseDto blogDto)
        {
            var rs = await _blogService.FindAllAndPaging(blogDto);
            return Ok(rs);
        }

        [HttpPost]
        public async Task<IActionResult> Add(CreateBlogDtoRequest blogDto)
        {
            await _blogService.Insert(blogDto);
            return NoContent();
        }

        [HttpPost("add-range")]
        public async Task<IActionResult> AddRange(List<CreateBlogDtoRequest> request)
        {
            await _blogService.AddRange(request);
            return NoContent();
        }
    }
}

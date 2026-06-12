using HW.Application.CQRS;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using Mapster;
using static HW.Application.Features.Blogs.Dtos.BlogDtos;

namespace HW.Application.Features.Blogs.Queries.GetBlogs;

public record GetBlogsQuery(string? Search, int PageIndex, int PageSize) : IQuery<PagedResult<BlogResponseDto>>;

public class GetBlogsQueryHandler : IQueryHandler<GetBlogsQuery, PagedResult<BlogResponseDto>>
{
    private readonly IEFRepository<Blog> _repository;

    public GetBlogsQueryHandler(IEFRepository<Blog> repository)
        => _repository = repository;

    public async Task<PagedResult<BlogResponseDto>> Handle(GetBlogsQuery request, CancellationToken ct)
    {
        var source = _repository.FindAll();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            source = source.Where(b =>
                ((string)b.Title).ToLower().Contains(search) ||
                ((string)b.Content).ToLower().Contains(search));
        }

        var pagedResult = await PagedResult<Blog>.CreateAsync(source, request.PageIndex, request.PageSize);
        return pagedResult.Adapt<PagedResult<BlogResponseDto>>();
    }
}

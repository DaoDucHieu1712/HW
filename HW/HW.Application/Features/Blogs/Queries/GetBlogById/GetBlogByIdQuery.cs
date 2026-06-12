using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using Mapster;
using static HW.Application.Features.Blogs.Dtos.BlogDtos;

namespace HW.Application.Features.Blogs.Queries.GetBlogById;

public record GetBlogByIdQuery(string Id) : IQuery<BlogResponseDto>;

public class GetBlogByIdQueryHandler : IQueryHandler<GetBlogByIdQuery, BlogResponseDto>
{
    private readonly IEFRepository<Blog> _repository;

    public GetBlogByIdQueryHandler(IEFRepository<Blog> repository)
        => _repository = repository;

    public async Task<BlogResponseDto> Handle(GetBlogByIdQuery request, CancellationToken ct)
    {
        var blog = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new BlogNotFoundException(request.Id);

        return blog.Adapt<BlogResponseDto>();
    }
}

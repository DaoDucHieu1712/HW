using HW.Application.Dtos;
using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using Mapster;
using Microsoft.EntityFrameworkCore;
using static HW.Application.Dtos.BlogDtos;

namespace HW.Application.Services;

public interface IBlogService
{
    Task<List<BlogResponseDto>> FindAll();
    Task<PagedResult<BlogResponseDto>> FindAllAndPaging(BlogPagingAndFilterResponseDto blogDto);
    Task<BlogResponseDto> FindById(string Id);
    Task Insert(CreateBlogDtoRequest blogDto);
    Task AddRange(List<CreateBlogDtoRequest> listdto);
    Task Update(UpdateBlogDtoRequest blogDto);
    Task Remove(string Id);

}

public class BlogService : IBlogService
{

    private readonly IEFRepository<Blog> _blogRepository;
    private readonly IUnitOfWork _uow;

    public BlogService(IEFRepository<Blog> blogRepository, IUnitOfWork uow)
    {
        _blogRepository = blogRepository;
        _uow = uow;
    }

    public async Task AddRange(List<CreateBlogDtoRequest> listdto)
    {
        await _uow.ExecuteAsync(async () =>
        {
            _blogRepository.AddRange(listdto.Adapt<List<Blog>>());
        });
    }

    public async Task<List<BlogResponseDto>> FindAll()
    {
        var blogs = await _blogRepository.FindAll().ToListAsync();
        return blogs.Adapt<List<BlogResponseDto>>();
    }

    public async Task<PagedResult<BlogResponseDto>> FindAllAndPaging(BlogPagingAndFilterResponseDto blogDto)
    {

        var source = _blogRepository
                        .FindAll();

        if (!string.IsNullOrWhiteSpace(blogDto.Search))
        {
            source = source.Where(b =>
                b.Title!.ToUpper().Contains(blogDto.Search!.ToUpper()) ||
                b.Content!.ToUpper().Contains(blogDto.Search!.ToUpper())
            );
        }

        var blogPagedList = await PagedResult<Blog>.CreateAsync(
            source, blogDto.PageIndex, blogDto.PageSize);


        return blogPagedList.Adapt<PagedResult<BlogResponseDto>>();
    }

    public async Task<BlogResponseDto> FindById(string Id)
    {
        var blog = await _blogRepository.FindByIdAsync(Id);
        return blog.Adapt<BlogResponseDto>();

    }

    public async Task Insert(CreateBlogDtoRequest blogDto)
    {
        await _uow.ExecuteAsync(async () =>
         {
             var newBlog = new Blog(blogDto.Title, blogDto.Content);
             _blogRepository.Add(newBlog);
         });
    }

    public async Task Remove(string Id)
    {
        await _uow.ExecuteAsync(async () =>
        {
            var blog = await _blogRepository.FindByIdAsync(Id);
            _blogRepository.Remove(blog);
        });
       
    }

    public async Task Update(UpdateBlogDtoRequest blogDto)
    {
        await _uow.ExecuteAsync(async () =>
        {
            var blog = await _blogRepository.FindByIdAsync(blogDto.Id);
            if (blogDto.Title != null) blog.Title = blogDto.Title;
            if (blogDto.Content != null) blog.Content = blogDto.Content;

            _blogRepository.Update(blog);
        });
    }
}


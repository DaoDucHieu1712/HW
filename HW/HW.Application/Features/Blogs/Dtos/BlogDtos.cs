namespace HW.Application.Features.Blogs.Dtos;

public static class BlogDtos
{
    public record BlogResponseDto(string Id, string? Title, string? Content, DateTimeOffset? CreatedAt,
        string? CreatedBy, DateTimeOffset? UpdatedAt, string? UpdatedBy);

    public record BlogPagingAndFilterResponseDto(string? Search, int PageIndex, int PageSize);

    public record CreateBlogRequestDto(string? Title, string? Content);

    public record UpdateBlogRequestDto(string Id, string? Title, string? Content);
}

using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using HW.Domain.ValueObjects;
using MediatR;

namespace HW.Application.Features.Blogs.Commands.UpdateBlog;

public record UpdateBlogCommand(string Id, string? Title, string? Content) : ICommand;

public class UpdateBlogCommandValidator : AbstractValidator<UpdateBlogCommand>
{
    public UpdateBlogCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).MaximumLength(BlogTitle.MaxLength).When(x => x.Title != null);
    }
}

public class UpdateBlogCommandHandler : ICommandHandler<UpdateBlogCommand>
{
    private readonly IRepository<Blog> _repository;

    public UpdateBlogCommandHandler(IRepository<Blog> repository)
        => _repository = repository;

    public async Task<Unit> Handle(UpdateBlogCommand request, CancellationToken ct)
    {
        var blog = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new BlogNotFoundException(request.Id);

        var title = request.Title is not null ? BlogTitle.Create(request.Title) : null;
        var content = request.Content is not null ? BlogContent.Create(request.Content) : null;
        blog.Update(title, content);
        _repository.Update(blog);
        return Unit.Value;
    }
}

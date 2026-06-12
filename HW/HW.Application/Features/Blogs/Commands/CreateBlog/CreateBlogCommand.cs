using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.ValueObjects;
using MediatR;

namespace HW.Application.Features.Blogs.Commands.CreateBlog;

public record CreateBlogCommand(string? Title, string? Content) : ICommand;

public class CreateBlogCommandValidator : AbstractValidator<CreateBlogCommand>
{
    public CreateBlogCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(BlogTitle.MaxLength);
        RuleFor(x => x.Content).NotEmpty();
    }
}

public class CreateBlogCommandHandler : ICommandHandler<CreateBlogCommand>
{
    private readonly IEFRepository<Blog> _repository;

    public CreateBlogCommandHandler(IEFRepository<Blog> repository)
        => _repository = repository;

    public Task<Unit> Handle(CreateBlogCommand request, CancellationToken ct)
    {
        var title = BlogTitle.Create(request.Title);
        var content = BlogContent.Create(request.Content);
        _repository.Add(new Blog(title, content));
        return Unit.Task;
    }
}

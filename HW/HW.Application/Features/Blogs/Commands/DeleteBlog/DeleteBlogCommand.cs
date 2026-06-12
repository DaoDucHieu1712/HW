using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.Blogs.Commands.DeleteBlog;

public record DeleteBlogCommand(string Id) : ICommand;

public class DeleteBlogCommandValidator : AbstractValidator<DeleteBlogCommand>
{
    public DeleteBlogCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteBlogCommandHandler : ICommandHandler<DeleteBlogCommand>
{
    private readonly IEFRepository<Blog> _repository;

    public DeleteBlogCommandHandler(IEFRepository<Blog> repository)
        => _repository = repository;

    public async Task<Unit> Handle(DeleteBlogCommand request, CancellationToken ct)
    {
        var blog = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new BlogNotFoundException(request.Id);

        blog.SoftDelete();
        _repository.Update(blog);
        return Unit.Value;
    }
}

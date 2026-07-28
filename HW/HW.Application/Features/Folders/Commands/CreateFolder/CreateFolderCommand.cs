using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using MediatR;

namespace HW.Application.Features.Folders.Commands.CreateFolder;

public record CreateFolderCommand(string Name, string? ParentId, string? Icon) : ICommand;

public class CreateFolderCommandValidator : AbstractValidator<CreateFolderCommand>
{
    public CreateFolderCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateFolderCommandHandler : ICommandHandler<CreateFolderCommand>
{
    private readonly IRepository<Folder> _repository;

    public CreateFolderCommandHandler(IRepository<Folder> repository)
        => _repository = repository;

    public Task<Unit> Handle(CreateFolderCommand request, CancellationToken ct)
    {
        _repository.Add(new Folder(request.Name, request.ParentId, request.Icon));
        return Unit.Task;
    }
}

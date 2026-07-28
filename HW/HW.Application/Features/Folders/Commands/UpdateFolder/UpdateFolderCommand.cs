using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.Folders.Commands.UpdateFolder;

public record UpdateFolderCommand(string Id, string? Name, string? Icon) : ICommand;

public class UpdateFolderCommandValidator : AbstractValidator<UpdateFolderCommand>
{
    public UpdateFolderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
    }
}

public class UpdateFolderCommandHandler : ICommandHandler<UpdateFolderCommand>
{
    private readonly IRepository<Folder> _repository;

    public UpdateFolderCommandHandler(IRepository<Folder> repository)
        => _repository = repository;

    public async Task<Unit> Handle(UpdateFolderCommand request, CancellationToken ct)
    {
        var folder = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new FolderNotFoundException(request.Id);

        folder.Update(request.Name, request.Icon);
        _repository.Update(folder);
        return Unit.Value;
    }
}

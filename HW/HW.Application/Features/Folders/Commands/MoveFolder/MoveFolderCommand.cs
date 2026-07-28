using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HW.Application.Features.Folders.Commands.MoveFolder;

public record MoveFolderCommand(string Id, string? NewParentId) : ICommand;

public class MoveFolderCommandValidator : AbstractValidator<MoveFolderCommand>
{
    public MoveFolderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class MoveFolderCommandHandler : ICommandHandler<MoveFolderCommand>
{
    private readonly IRepository<Folder> _repository;

    public MoveFolderCommandHandler(IRepository<Folder> repository)
        => _repository = repository;

    public async Task<Unit> Handle(MoveFolderCommand request, CancellationToken ct)
    {
        var folder = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new FolderNotFoundException(request.Id);

        if (request.NewParentId is not null)
        {
            var parentExists = await _repository.FindAll(f => f.Id == request.NewParentId).AnyAsync(ct);
            if (!parentExists) throw new FolderNotFoundException(request.NewParentId);
        }

        folder.Move(request.NewParentId);
        _repository.Update(folder);
        return Unit.Value;
    }
}

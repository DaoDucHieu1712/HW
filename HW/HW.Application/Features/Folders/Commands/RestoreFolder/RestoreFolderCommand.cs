using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HW.Application.Features.Folders.Commands.RestoreFolder;

public record RestoreFolderCommand(string Id) : ICommand;

public class RestoreFolderCommandValidator : AbstractValidator<RestoreFolderCommand>
{
    public RestoreFolderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class RestoreFolderCommandHandler : ICommandHandler<RestoreFolderCommand>
{
    private readonly IRepository<Folder> _repository;

    public RestoreFolderCommandHandler(IRepository<Folder> repository)
        => _repository = repository;

    public async Task<Unit> Handle(RestoreFolderCommand request, CancellationToken ct)
    {
        var folder = await _repository.FindAllIgnoreFilters(f => f.Id == request.Id && f.IsDelete == true)
            .FirstOrDefaultAsync(ct)
            ?? throw new FolderNotFoundException(request.Id);

        // If the original parent is also in trash, restore to root instead
        if (folder.ParentId is not null)
        {
            var parentAlive = await _repository.FindAll(f => f.Id == folder.ParentId).AnyAsync(ct);
            if (!parentAlive)
                folder.Move(null);
        }

        folder.Restore();
        _repository.Update(folder);
        return Unit.Value;
    }
}

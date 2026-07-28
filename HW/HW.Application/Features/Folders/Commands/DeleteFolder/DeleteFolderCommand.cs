using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HW.Application.Features.Folders.Commands.DeleteFolder;

public record DeleteFolderCommand(string Id) : ICommand;

public class DeleteFolderCommandValidator : AbstractValidator<DeleteFolderCommand>
{
    public DeleteFolderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteFolderCommandHandler : ICommandHandler<DeleteFolderCommand>
{
    private readonly IRepository<Folder> _folderRepository;
    private readonly IRepository<Note> _noteRepository;

    public DeleteFolderCommandHandler(IRepository<Folder> folderRepository, IRepository<Note> noteRepository)
    {
        _folderRepository = folderRepository;
        _noteRepository = noteRepository;
    }

    public async Task<Unit> Handle(DeleteFolderCommand request, CancellationToken ct)
    {
        var exists = await _folderRepository.FindAll(f => f.Id == request.Id).AnyAsync(ct);
        if (!exists) throw new FolderNotFoundException(request.Id);

        // Load all non-deleted folders to compute the subtree in memory
        var allFolders = await _folderRepository.FindAll().ToListAsync(ct);
        var toDeleteIds = new HashSet<string> { request.Id };
        CollectDescendants(allFolders, request.Id, toDeleteIds);

        foreach (var folder in allFolders.Where(f => toDeleteIds.Contains(f.Id)))
        {
            folder.SoftDelete();
            _folderRepository.Update(folder);
        }

        var notes = await _noteRepository.FindAll(n => toDeleteIds.Contains(n.FolderId!)).ToListAsync(ct);
        foreach (var note in notes)
        {
            note.SoftDelete();
            _noteRepository.Update(note);
        }

        return Unit.Value;
    }

    private static void CollectDescendants(List<Folder> all, string parentId, HashSet<string> result)
    {
        foreach (var child in all.Where(f => f.ParentId == parentId))
        {
            result.Add(child.Id);
            CollectDescendants(all, child.Id, result);
        }
    }
}

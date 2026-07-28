using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HW.Application.Features.Notes.Commands.MoveNote;

public record MoveNoteCommand(string Id, string? FolderId) : ICommand;

public class MoveNoteCommandValidator : AbstractValidator<MoveNoteCommand>
{
    public MoveNoteCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class MoveNoteCommandHandler : ICommandHandler<MoveNoteCommand>
{
    private readonly IRepository<Note> _noteRepository;
    private readonly IRepository<Folder> _folderRepository;

    public MoveNoteCommandHandler(IRepository<Note> noteRepository, IRepository<Folder> folderRepository)
    {
        _noteRepository = noteRepository;
        _folderRepository = folderRepository;
    }

    public async Task<Unit> Handle(MoveNoteCommand request, CancellationToken ct)
    {
        var note = await _noteRepository.FindByIdAsync(request.Id, ct)
            ?? throw new NoteNotFoundException(request.Id);

        if (request.FolderId is not null)
        {
            var folderExists = await _folderRepository.FindAll(f => f.Id == request.FolderId).AnyAsync(ct);
            if (!folderExists) throw new FolderNotFoundException(request.FolderId);
        }

        note.Move(request.FolderId);
        _noteRepository.Update(note);
        return Unit.Value;
    }
}

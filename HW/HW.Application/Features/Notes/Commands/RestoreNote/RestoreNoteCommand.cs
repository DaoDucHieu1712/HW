using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HW.Application.Features.Notes.Commands.RestoreNote;

public record RestoreNoteCommand(string Id) : ICommand;

public class RestoreNoteCommandValidator : AbstractValidator<RestoreNoteCommand>
{
    public RestoreNoteCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class RestoreNoteCommandHandler : ICommandHandler<RestoreNoteCommand>
{
    private readonly IRepository<Note> _noteRepository;
    private readonly IRepository<Folder> _folderRepository;

    public RestoreNoteCommandHandler(IRepository<Note> noteRepository, IRepository<Folder> folderRepository)
    {
        _noteRepository = noteRepository;
        _folderRepository = folderRepository;
    }

    public async Task<Unit> Handle(RestoreNoteCommand request, CancellationToken ct)
    {
        var note = await _noteRepository.FindAllIgnoreFilters(n => n.Id == request.Id && n.IsDelete == true)
            .FirstOrDefaultAsync(ct)
            ?? throw new NoteNotFoundException(request.Id);

        // If the original folder is also in trash, restore the note to root instead
        if (note.FolderId is not null)
        {
            var folderAlive = await _folderRepository.FindAll(f => f.Id == note.FolderId).AnyAsync(ct);
            if (!folderAlive)
                note.Move(null);
        }

        note.Restore();
        _noteRepository.Update(note);
        return Unit.Value;
    }
}

using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.Notes.Commands.DeleteNote;

public record DeleteNoteCommand(string Id) : ICommand;

public class DeleteNoteCommandValidator : AbstractValidator<DeleteNoteCommand>
{
    public DeleteNoteCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteNoteCommandHandler : ICommandHandler<DeleteNoteCommand>
{
    private readonly IRepository<Note> _repository;

    public DeleteNoteCommandHandler(IRepository<Note> repository)
        => _repository = repository;

    public async Task<Unit> Handle(DeleteNoteCommand request, CancellationToken ct)
    {
        var note = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new NoteNotFoundException(request.Id);

        note.SoftDelete();
        _repository.Update(note);
        return Unit.Value;
    }
}

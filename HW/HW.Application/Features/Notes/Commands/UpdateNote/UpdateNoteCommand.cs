using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.Notes.Commands.UpdateNote;

public record UpdateNoteCommand(string Id, string? Title, string? Content) : ICommand;

public class UpdateNoteCommandValidator : AbstractValidator<UpdateNoteCommand>
{
    public UpdateNoteCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).MaximumLength(500).When(x => x.Title is not null);
    }
}

public class UpdateNoteCommandHandler : ICommandHandler<UpdateNoteCommand>
{
    private readonly IRepository<Note> _repository;

    public UpdateNoteCommandHandler(IRepository<Note> repository)
        => _repository = repository;

    public async Task<Unit> Handle(UpdateNoteCommand request, CancellationToken ct)
    {
        var note = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new NoteNotFoundException(request.Id);

        note.Update(request.Title, null, null, null, request.Content);
        _repository.Update(note);
        return Unit.Value;
    }
}

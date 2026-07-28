using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Enums;
using MediatR;

namespace HW.Application.Features.Notes.Commands.CreateNote;

public record CreateNoteCommand(string? Title, string? Content, string? FolderId) : ICommand;

public class CreateNoteCommandValidator : AbstractValidator<CreateNoteCommand>
{
    public CreateNoteCommandValidator()
    {
        RuleFor(x => x.Title).MaximumLength(500).When(x => x.Title is not null);
    }
}

public class CreateNoteCommandHandler : ICommandHandler<CreateNoteCommand>
{
    private readonly IRepository<Note> _repository;

    public CreateNoteCommandHandler(IRepository<Note> repository)
        => _repository = repository;

    public Task<Unit> Handle(CreateNoteCommand request, CancellationToken ct)
    {
        _repository.Add(new Note(request.Title, null, null, null, request.Content, NoteType.English, request.FolderId));
        return Unit.Task;
    }
}

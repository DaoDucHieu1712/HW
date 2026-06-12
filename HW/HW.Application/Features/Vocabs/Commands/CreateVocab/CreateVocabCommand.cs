using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using MediatR;

namespace HW.Application.Features.Vocabs.Commands.CreateVocab;

public record CreateVocabCommand(string? Word, string? Meaning, string? Example, string? Note) : ICommand;

public class CreateVocabCommandValidator : AbstractValidator<CreateVocabCommand>
{
    public CreateVocabCommandValidator()
    {
        RuleFor(x => x.Word).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Meaning).MaximumLength(500).When(x => x.Meaning is not null);
        RuleFor(x => x.Example).MaximumLength(1000).When(x => x.Example is not null);
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}

public class CreateVocabCommandHandler : ICommandHandler<CreateVocabCommand>
{
    private readonly IEFRepository<Vocab> _repository;

    public CreateVocabCommandHandler(IEFRepository<Vocab> repository)
        => _repository = repository;

    public Task<Unit> Handle(CreateVocabCommand request, CancellationToken ct)
    {
        _repository.Add(new Vocab(request.Word!, request.Meaning, request.Example, request.Note));
        return Unit.Task;
    }
}

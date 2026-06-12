using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.Vocabs.Commands.UpdateVocab;

public record UpdateVocabCommand(string Id, string? Word, string? Meaning, string? Example, string? Note) : ICommand;

public class UpdateVocabCommandValidator : AbstractValidator<UpdateVocabCommand>
{
    public UpdateVocabCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Word).MaximumLength(200).When(x => x.Word is not null);
        RuleFor(x => x.Meaning).MaximumLength(500).When(x => x.Meaning is not null);
        RuleFor(x => x.Example).MaximumLength(1000).When(x => x.Example is not null);
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}

public class UpdateVocabCommandHandler : ICommandHandler<UpdateVocabCommand>
{
    private readonly IEFRepository<Vocab> _repository;

    public UpdateVocabCommandHandler(IEFRepository<Vocab> repository)
        => _repository = repository;

    public async Task<Unit> Handle(UpdateVocabCommand request, CancellationToken ct)
    {
        var vocab = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new VocabNotFoundException(request.Id);

        vocab.Update(request.Word, request.Meaning, request.Example, request.Note);
        _repository.Update(vocab);
        return Unit.Value;
    }
}

using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using HW.Domain.ValueObjects;
using MediatR;

namespace HW.Application.Features.Vocabs.Commands.UpdateVocab;

public record UpdateVocabCommand(string Id, string? Word, string? Content) : ICommand;

public class UpdateVocabCommandValidator : AbstractValidator<UpdateVocabCommand>
{
    public UpdateVocabCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Word).MaximumLength(200).When(x => x.Word is not null);
    }
}

public class UpdateVocabCommandHandler : ICommandHandler<UpdateVocabCommand>
{
    private readonly IRepository<Vocab> _repository;

    public UpdateVocabCommandHandler(IRepository<Vocab> repository)
        => _repository = repository;

    public async Task<Unit> Handle(UpdateVocabCommand request, CancellationToken ct)
    {
        var vocab = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new VocabNotFoundException(request.Id);

        var word = request.Word is not null ? Word.Create(request.Word) : null;
        vocab.Update(word, request.Content);
        _repository.Update(vocab);
        return Unit.Value;
    }
}

using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.ValueObjects;
using MediatR;

namespace HW.Application.Features.Vocabs.Commands.CreateVocab;

public record CreateVocabCommand(string? Word, string? Content) : ICommand;

public class CreateVocabCommandValidator : AbstractValidator<CreateVocabCommand>
{
    public CreateVocabCommandValidator()
    {
        RuleFor(x => x.Word).NotEmpty().MaximumLength(200);
    }
}

public class CreateVocabCommandHandler : ICommandHandler<CreateVocabCommand>
{
    private readonly IRepository<Vocab> _repository;

    public CreateVocabCommandHandler(IRepository<Vocab> repository)
        => _repository = repository;

    public Task<Unit> Handle(CreateVocabCommand request, CancellationToken ct)
    {
        var word = Word.Create(request.Word);
        _repository.Add(new Vocab(word, request.Content));
        return Unit.Task;
    }
}

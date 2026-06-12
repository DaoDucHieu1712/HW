using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.Vocabs.Commands.ReviewVocab;

public record ReviewVocabCommand(string Id) : ICommand;

public class ReviewVocabCommandValidator : AbstractValidator<ReviewVocabCommand>
{
    public ReviewVocabCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class ReviewVocabCommandHandler : ICommandHandler<ReviewVocabCommand>
{
    private readonly IEFRepository<Vocab> _repository;

    public ReviewVocabCommandHandler(IEFRepository<Vocab> repository)
        => _repository = repository;

    public async Task<Unit> Handle(ReviewVocabCommand request, CancellationToken ct)
    {
        var vocab = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new VocabNotFoundException(request.Id);

        vocab.MarkReviewed();
        _repository.Update(vocab);
        return Unit.Value;
    }
}

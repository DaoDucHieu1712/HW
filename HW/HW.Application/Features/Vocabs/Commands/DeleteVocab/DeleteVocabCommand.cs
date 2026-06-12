using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.Vocabs.Commands.DeleteVocab;

public record DeleteVocabCommand(string Id) : ICommand;

public class DeleteVocabCommandValidator : AbstractValidator<DeleteVocabCommand>
{
    public DeleteVocabCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteVocabCommandHandler : ICommandHandler<DeleteVocabCommand>
{
    private readonly IEFRepository<Vocab> _repository;

    public DeleteVocabCommandHandler(IEFRepository<Vocab> repository)
        => _repository = repository;

    public async Task<Unit> Handle(DeleteVocabCommand request, CancellationToken ct)
    {
        var vocab = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new VocabNotFoundException(request.Id);

        vocab.SoftDelete();
        _repository.Update(vocab);
        return Unit.Value;
    }
}

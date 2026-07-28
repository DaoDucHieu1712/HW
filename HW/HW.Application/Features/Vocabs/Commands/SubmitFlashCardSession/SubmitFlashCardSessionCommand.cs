using FluentValidation;
using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;

namespace HW.Application.Features.Vocabs.Commands.SubmitFlashCardSession;

public record SubmitFlashCardSessionCommand(List<VocabDtos.FlashCardResultRequestDto> Results)
    : ICommand<VocabDtos.FlashCardSessionResultDto>;

public class SubmitFlashCardSessionCommandValidator : AbstractValidator<SubmitFlashCardSessionCommand>
{
    public SubmitFlashCardSessionCommandValidator()
    {
        RuleFor(x => x.Results).NotEmpty();
        RuleForEach(x => x.Results).ChildRules(r =>
        {
            r.RuleFor(x => x.VocabId).NotEmpty();
        });
    }
}

public class SubmitFlashCardSessionCommandHandler
    : ICommandHandler<SubmitFlashCardSessionCommand, VocabDtos.FlashCardSessionResultDto>
{
    private readonly IRepository<Vocab> _repository;

    public SubmitFlashCardSessionCommandHandler(IRepository<Vocab> repository)
        => _repository = repository;

    public async Task<VocabDtos.FlashCardSessionResultDto> Handle(
        SubmitFlashCardSessionCommand request, CancellationToken ct)
    {
        var advancedIds = new List<string>();

        foreach (var result in request.Results.Where(r => r.Knew))
        {
            var vocab = await _repository.FindByIdAsync(result.VocabId, ct);
            if (vocab is null || vocab.IsCompleted) continue;

            vocab.MarkReviewed();
            _repository.Update(vocab);
            advancedIds.Add(vocab.Id);
        }

        var knewCount = request.Results.Count(r => r.Knew);

        return new VocabDtos.FlashCardSessionResultDto(
            TotalCards: request.Results.Count,
            KnewCount: knewCount,
            DidntKnowCount: request.Results.Count - knewCount,
            AdvancedVocabIds: advancedIds);
    }
}

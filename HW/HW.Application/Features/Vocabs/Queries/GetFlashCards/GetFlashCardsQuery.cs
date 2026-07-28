using FluentValidation;
using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HW.Application.Features.Vocabs.Queries.GetFlashCards;

public record GetFlashCardsQuery(int? Count, int? ReviewStage, bool UseDaily) : IQuery<List<VocabDtos.FlashCardDto>>;

public class GetFlashCardsQueryValidator : AbstractValidator<GetFlashCardsQuery>
{
    public GetFlashCardsQueryValidator()
    {
        RuleFor(x => x.Count).GreaterThan(0).When(x => x.Count.HasValue);
    }
}

public class GetFlashCardsQueryHandler : IQueryHandler<GetFlashCardsQuery, List<VocabDtos.FlashCardDto>>
{
    private readonly IRepository<Vocab> _repository;

    public GetFlashCardsQueryHandler(IRepository<Vocab> repository)
        => _repository = repository;

    public async Task<List<VocabDtos.FlashCardDto>> Handle(GetFlashCardsQuery request, CancellationToken ct)
    {
        var query = _repository.FindAll();

        if (request.UseDaily)
        {
            var startOfTomorrow = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(1), TimeSpan.Zero);
            query = query.Where(x => x.ReviewStage != ReviewStage.Mastered && x.NextReviewAt < startOfTomorrow);
        }

        if (request.ReviewStage.HasValue)
            query = query.Where(x => x.ReviewStage == (ReviewStage)request.ReviewStage.Value);

        var vocabs = await query.ToListAsync(ct);

        var shuffled = vocabs.OrderBy(_ => Guid.NewGuid());
        var deck = request.Count.HasValue ? shuffled.Take(request.Count.Value) : shuffled;

        return deck.Select(v => new VocabDtos.FlashCardDto(
            v.Id,
            v.Word.Value,
            v.Content,
            (int)v.ReviewStage
        )).ToList();
    }
}

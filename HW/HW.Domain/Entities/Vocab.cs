using HW.Domain.Abstractions.Entities;
using HW.Domain.Enums;
using HW.Domain.Events.Vocabs;
using HW.Domain.Exceptions;
using HW.Domain.ValueObjects;

namespace HW.Domain.Entities;

public class Vocab : AggregateRoot, IAuditableEntity, ISoftDeleteEntity
{
    private static readonly Dictionary<ReviewStage, int> ReviewIntervalDays = new()
    {
        [ReviewStage.New] = 3,
        [ReviewStage.Reviewed] = 7,
        [ReviewStage.Reinforced] = 14,
    };

    protected Vocab() { }

    public Vocab(Word word, string? content)
    {
        Word = word;
        Content = content;
        NotedAt = DateTimeOffset.UtcNow;
        ReviewStage = ReviewStage.New;
        NextReviewAt = NotedAt.AddDays(ReviewIntervalDays[ReviewStage.New]);
        RaiseDomainEvent(new VocabCreatedDomainEvent(Id, word));
    }

    public Word Word { get; private set; } = null!;
    public string? Content { get; private set; }
    public DateTimeOffset NotedAt { get; private set; }
    public ReviewStage ReviewStage { get; private set; }
    public DateTimeOffset? NextReviewAt { get; private set; }
    public DateTimeOffset? LastReviewedAt { get; private set; }

    public bool IsCompleted => ReviewStage == ReviewStage.Mastered;

    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }

    public void Update(Word? word, string? content)
    {
        if (word is not null) Word = word;
        if (content is not null) Content = content;
        RaiseDomainEvent(new VocabUpdatedDomainEvent(Id, Word));
    }

    public void MarkReviewed()
    {
        if (IsCompleted)
            throw new BadRequestException($"Vocab '{Word}' has already completed all review stages.");

        LastReviewedAt = DateTimeOffset.UtcNow;
        var nextStage = (ReviewStage)((int)this.ReviewStage + 1);
        ReviewStage = nextStage;

        NextReviewAt = ReviewIntervalDays.TryGetValue(this.ReviewStage, out var days)
            ? NotedAt.AddDays(days)
            : null;

        RaiseDomainEvent(new VocabReviewedDomainEvent(Id, this.ReviewStage, NextReviewAt));
    }

    public void SoftDelete()
    {
        IsDelete = true;
        RaiseDomainEvent(new VocabDeletedDomainEvent(Id));
    }
}

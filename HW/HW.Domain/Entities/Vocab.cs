using HW.Domain.Abstractions.Entities;
using HW.Domain.Events.Vocabs;
using HW.Domain.Exceptions;

namespace HW.Domain.Entities;

public class Vocab : AggregateRoot, IAuditableEntity, ISoftDeleteEntity
{
    private static readonly int[] ReviewIntervalDays = [3, 7, 14];

    protected Vocab() { }

    public Vocab(string word, string? meaning, string? example, string? note)
    {
        Word = word;
        Meaning = meaning;
        Example = example;
        Note = note;
        NotedAt = DateTimeOffset.UtcNow;
        ReviewStage = 0;
        NextReviewAt = NotedAt.AddDays(ReviewIntervalDays[0]);
        RaiseDomainEvent(new VocabCreatedDomainEvent(Id, word));
    }

    public string Word { get; private set; } = string.Empty;
    public string? Meaning { get; private set; }
    public string? Example { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset NotedAt { get; private set; }
    public int ReviewStage { get; private set; }
    public DateTimeOffset? NextReviewAt { get; private set; }
    public DateTimeOffset? LastReviewedAt { get; private set; }

    public bool IsCompleted => ReviewStage >= ReviewIntervalDays.Length;

    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }

    public void Update(string? word, string? meaning, string? example, string? note)
    {
        if (word is not null) Word = word;
        if (meaning is not null) Meaning = meaning;
        if (example is not null) Example = example;
        if (note is not null) Note = note;
        RaiseDomainEvent(new VocabUpdatedDomainEvent(Id, Word));
    }

    public void MarkReviewed()
    {
        if (IsCompleted)
            throw new BadRequestException($"Vocab '{Word}' has already completed all review stages.");

        LastReviewedAt = DateTimeOffset.UtcNow;
        ReviewStage++;

        NextReviewAt = ReviewStage < ReviewIntervalDays.Length
            ? NotedAt.AddDays(ReviewIntervalDays[ReviewStage])
            : null;

        RaiseDomainEvent(new VocabReviewedDomainEvent(Id, ReviewStage, NextReviewAt));
    }

    public void SoftDelete()
    {
        IsDelete = true;
        RaiseDomainEvent(new VocabDeletedDomainEvent(Id));
    }
}

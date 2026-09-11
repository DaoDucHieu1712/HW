using System.Reflection;
using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.ValueObjects;

namespace HW.UnitTests.TestSupport;

/// <summary>
/// Builds a <see cref="Vocab"/> in a state a test needs.
///
/// <para>
/// <c>NotedAt</c> and the review schedule are private to the aggregate — deliberately, since the
/// domain owns them — so the builder writes them through their non-public setters rather than
/// widening the entity for the sake of tests. Everything else goes through the real behavior:
/// a word at stage <see cref="ReviewStage.Reinforced"/> is one that had
/// <see cref="Vocab.MarkReviewed"/> called twice, not one with a field poked to 2.
/// </para>
/// </summary>
public sealed class VocabBuilder
{
    private string _word = "serendipity";
    private string? _content = "(n) sự tình cờ may mắn — a happy accident. 'a series of happy serendipities'";
    private DateTimeOffset _notedAt = DateTimeOffset.UtcNow.AddDays(-30);
    private DateTimeOffset? _createdAt;
    private DateTimeOffset? _nextReviewAtOverride;
    private ReviewStage _stage = ReviewStage.New;
    private bool _softDeleted;
    private string? _id;

    public static VocabBuilder A() => new();

    public VocabBuilder WithWord(string word)
    {
        _word = word;
        return this;
    }

    public VocabBuilder WithContent(string? content)
    {
        _content = content;
        return this;
    }

    public VocabBuilder NotedAt(DateTimeOffset notedAt)
    {
        _notedAt = notedAt;
        return this;
    }

    public VocabBuilder CreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public VocabBuilder AtStage(ReviewStage stage)
    {
        _stage = stage;
        return this;
    }

    /// <summary>
    /// Forces the next review date, for the cases the schedule cannot reach on its own — a Mastered
    /// word that still carries a due date, say, which is what proves the daily mission filters on
    /// the stage and not merely on the date.
    /// </summary>
    public VocabBuilder NextReviewAt(DateTimeOffset? nextReviewAt)
    {
        _nextReviewAtOverride = nextReviewAt;
        return this;
    }

    public VocabBuilder SoftDeleted()
    {
        _softDeleted = true;
        return this;
    }

    public VocabBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public Vocab Build()
    {
        var vocab = new Vocab(Word.Create(_word), _content);

        SetPrivate(vocab, nameof(Vocab.NotedAt), _notedAt);
        SetPrivate(vocab, nameof(Vocab.NextReviewAt), (DateTimeOffset?)_notedAt.AddDays(3));

        for (var stage = ReviewStage.New; stage < _stage; stage++)
            vocab.MarkReviewed();

        if (_nextReviewAtOverride is not null)
            SetPrivate(vocab, nameof(Vocab.NextReviewAt), _nextReviewAtOverride);

        if (_softDeleted)
            vocab.SoftDelete();

        if (_id is not null)
            vocab.Id = _id;

        // Set by the unit of work in production; the paging query orders on it.
        vocab.CreatedAt = _createdAt ?? _notedAt;

        // Setup is not what a test is asserting on.
        vocab.ClearDomainEvents();

        return vocab;
    }

    private static void SetPrivate(Vocab vocab, string propertyName, object? value)
    {
        var setter = typeof(Vocab)
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!;

        setter.Invoke(vocab, [value]);
    }
}

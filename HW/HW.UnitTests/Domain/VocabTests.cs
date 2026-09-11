using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.Events.Vocabs;
using HW.Domain.Exceptions;
using HW.Domain.ValueObjects;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Domain;

public class VocabTests
{
    [Fact]
    public void A_new_word_starts_at_stage_New()
    {
        var before = DateTimeOffset.UtcNow;

        var vocab = new Vocab(Word.Create("serendipity"), "a happy accident");

        Assert.Equal("serendipity", vocab.Word.Value);
        Assert.Equal("a happy accident", vocab.Content);
        Assert.Equal(ReviewStage.New, vocab.ReviewStage);
        Assert.False(vocab.IsCompleted);
        Assert.Null(vocab.LastReviewedAt);
        Assert.InRange(vocab.NotedAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void A_new_word_is_due_three_days_after_it_was_noted()
    {
        var vocab = new Vocab(Word.Create("serendipity"), null);

        Assert.Equal(vocab.NotedAt.AddDays(3), vocab.NextReviewAt);
    }

    [Fact]
    public void A_new_word_may_be_saved_without_a_meaning()
    {
        var vocab = new Vocab(Word.Create("serendipity"), null);

        Assert.Null(vocab.Content);
    }

    [Fact]
    public void Creating_a_word_raises_the_created_event()
    {
        var vocab = new Vocab(Word.Create("serendipity"), "a happy accident");

        var raised = Assert.IsType<VocabCreatedDomainEvent>(Assert.Single(vocab.DomainEvents));
        Assert.Equal(vocab.Id, raised.VocabId);
        Assert.Equal("serendipity", raised.Word);
    }

    [Fact]
    public void Update_writes_both_fields_and_raises_the_updated_event()
    {
        var vocab = VocabBuilder.A().WithWord("recieve").WithContent("old meaning").Build();

        vocab.Update(Word.Create("receive"), "new meaning");

        Assert.Equal("receive", vocab.Word.Value);
        Assert.Equal("new meaning", vocab.Content);

        var raised = Assert.IsType<VocabUpdatedDomainEvent>(Assert.Single(vocab.DomainEvents));
        Assert.Equal(vocab.Id, raised.VocabId);
        Assert.Equal("receive", raised.Word);
    }

    [Fact]
    public void Update_leaves_out_what_was_not_supplied()
    {
        var vocab = VocabBuilder.A().WithWord("ephemeral").WithContent("lasting a short time").Build();

        vocab.Update(word: null, content: "(adj) lasting a very short time");

        Assert.Equal("ephemeral", vocab.Word.Value);
        Assert.Equal("(adj) lasting a very short time", vocab.Content);

        vocab.Update(Word.Create("ephemerality"), content: null);

        Assert.Equal("ephemerality", vocab.Word.Value);
        Assert.Equal("(adj) lasting a very short time", vocab.Content);
    }

    [Fact]
    public void Update_does_not_touch_the_review_schedule()
    {
        var vocab = VocabBuilder.A().AtStage(ReviewStage.Reviewed).Build();
        var stage = vocab.ReviewStage;
        var due = vocab.NextReviewAt;

        vocab.Update(Word.Create("something else"), "something else entirely");

        Assert.Equal(stage, vocab.ReviewStage);
        Assert.Equal(due, vocab.NextReviewAt);
    }

    [Theory]
    [InlineData(ReviewStage.New, ReviewStage.Reviewed, 7)]
    [InlineData(ReviewStage.Reviewed, ReviewStage.Reinforced, 14)]
    public void MarkReviewed_advances_one_stage_and_reschedules_from_the_noted_date(
        ReviewStage from, ReviewStage to, int daysAfterNoting)
    {
        var notedAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var vocab = VocabBuilder.A().NotedAt(notedAt).AtStage(from).Build();
        var before = DateTimeOffset.UtcNow;

        vocab.MarkReviewed();

        Assert.Equal(to, vocab.ReviewStage);
        Assert.Equal(notedAt.AddDays(daysAfterNoting), vocab.NextReviewAt);
        Assert.NotNull(vocab.LastReviewedAt);
        Assert.InRange(vocab.LastReviewedAt!.Value, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void The_last_review_masters_the_word_and_stops_scheduling_it()
    {
        var vocab = VocabBuilder.A().AtStage(ReviewStage.Reinforced).Build();

        vocab.MarkReviewed();

        Assert.Equal(ReviewStage.Mastered, vocab.ReviewStage);
        Assert.True(vocab.IsCompleted);
        Assert.Null(vocab.NextReviewAt);
    }

    [Fact]
    public void MarkReviewed_raises_the_reviewed_event_carrying_the_new_schedule()
    {
        var notedAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var vocab = VocabBuilder.A().NotedAt(notedAt).Build();

        vocab.MarkReviewed();

        var raised = Assert.IsType<VocabReviewedDomainEvent>(Assert.Single(vocab.DomainEvents));
        Assert.Equal(vocab.Id, raised.VocabId);
        Assert.Equal(ReviewStage.Reviewed, raised.NewStage);
        Assert.Equal(notedAt.AddDays(7), raised.NextReviewAt);
    }

    [Fact]
    public void A_mastered_word_cannot_be_reviewed_again()
    {
        var vocab = VocabBuilder.A().WithWord("perennial").AtStage(ReviewStage.Mastered).Build();

        var error = Assert.Throws<BadRequestException>(vocab.MarkReviewed);

        Assert.Equal("Vocab 'perennial' has already completed all review stages.", error.Message);
        Assert.Empty(vocab.DomainEvents);
    }

    [Fact]
    public void The_whole_ladder_runs_New_to_Mastered_in_three_reviews()
    {
        var vocab = VocabBuilder.A().Build();

        vocab.MarkReviewed();
        vocab.MarkReviewed();
        vocab.MarkReviewed();

        Assert.Equal(ReviewStage.Mastered, vocab.ReviewStage);
        Assert.Equal(3, vocab.DomainEvents.Count);
        Assert.All(vocab.DomainEvents, e => Assert.IsType<VocabReviewedDomainEvent>(e));
    }

    [Fact]
    public void SoftDelete_flags_the_word_and_raises_the_deleted_event()
    {
        var vocab = VocabBuilder.A().Build();

        vocab.SoftDelete();

        Assert.True(vocab.IsDelete);

        var raised = Assert.IsType<VocabDeletedDomainEvent>(Assert.Single(vocab.DomainEvents));
        Assert.Equal(vocab.Id, raised.VocabId);
    }

    [Fact]
    public void ClearDomainEvents_empties_the_queue()
    {
        var vocab = new Vocab(Word.Create("serendipity"), null);
        Assert.NotEmpty(vocab.DomainEvents);

        vocab.ClearDomainEvents();

        Assert.Empty(vocab.DomainEvents);
    }

    [Fact]
    public void Domain_events_accumulate_in_the_order_they_were_raised()
    {
        var vocab = new Vocab(Word.Create("serendipity"), null);

        vocab.Update(null, "a happy accident");
        vocab.MarkReviewed();
        vocab.SoftDelete();

        Assert.Collection(
            vocab.DomainEvents,
            e => Assert.IsType<VocabCreatedDomainEvent>(e),
            e => Assert.IsType<VocabUpdatedDomainEvent>(e),
            e => Assert.IsType<VocabReviewedDomainEvent>(e),
            e => Assert.IsType<VocabDeletedDomainEvent>(e));
    }
}

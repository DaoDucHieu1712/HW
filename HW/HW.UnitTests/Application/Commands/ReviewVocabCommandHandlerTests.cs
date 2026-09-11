using HW.Application.Features.Vocabs.Commands.ReviewVocab;
using HW.Domain.Enums;
using HW.Domain.Events.Vocabs;
using HW.Domain.Exceptions;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Commands;

public class ReviewVocabCommandHandlerTests
{
    [Fact]
    public async Task Advances_the_word_one_stage_and_reschedules_it()
    {
        var notedAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().NotedAt(notedAt).Build();
        await harness.SeedAsync(vocab);

        var handler = new ReviewVocabCommandHandler(harness.Repository);
        await handler.Handle(new ReviewVocabCommand(vocab.Id), default);
        await harness.SaveAsync();

        var reviewed = await harness.ReloadAsync(vocab.Id);
        Assert.Equal(ReviewStage.Reviewed, reviewed!.ReviewStage);
        Assert.Equal(notedAt.AddDays(7), reviewed.NextReviewAt);
        Assert.NotNull(reviewed.LastReviewedAt);
    }

    [Fact]
    public async Task The_last_review_masters_the_word()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().AtStage(ReviewStage.Reinforced).Build();
        await harness.SeedAsync(vocab);

        var handler = new ReviewVocabCommandHandler(harness.Repository);
        await handler.Handle(new ReviewVocabCommand(vocab.Id), default);
        await harness.SaveAsync();

        var reviewed = await harness.ReloadAsync(vocab.Id);
        Assert.Equal(ReviewStage.Mastered, reviewed!.ReviewStage);
        Assert.True(reviewed.IsCompleted);
        Assert.Null(reviewed.NextReviewAt);
    }

    [Fact]
    public async Task Raises_the_reviewed_event()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().Build();
        await harness.SeedAsync(vocab);

        var handler = new ReviewVocabCommandHandler(harness.Repository);
        await handler.Handle(new ReviewVocabCommand(vocab.Id), default);

        var tracked = Assert.Single(harness.Db.ChangeTracker.Entries<HW.Domain.Entities.Vocab>()).Entity;
        var raised = Assert.IsType<VocabReviewedDomainEvent>(Assert.Single(tracked.DomainEvents));
        Assert.Equal(ReviewStage.Reviewed, raised.NewStage);
    }

    [Fact]
    public async Task Refuses_to_review_a_mastered_word()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithWord("perennial").AtStage(ReviewStage.Mastered).Build();
        await harness.SeedAsync(vocab);

        var handler = new ReviewVocabCommandHandler(harness.Repository);

        var error = await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(new ReviewVocabCommand(vocab.Id), default));

        Assert.Contains("perennial", error.Message);
    }

    [Fact]
    public async Task Reports_an_unknown_id_as_not_found()
    {
        using var harness = new VocabTestHarness();
        var handler = new ReviewVocabCommandHandler(harness.Repository);

        await Assert.ThrowsAsync<VocabNotFoundException>(
            () => handler.Handle(new ReviewVocabCommand("missing-id"), default));
    }

    [Fact]
    public async Task Treats_a_soft_deleted_word_as_gone()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().SoftDeleted().Build();
        await harness.SeedAsync(vocab);

        var handler = new ReviewVocabCommandHandler(harness.Repository);

        await Assert.ThrowsAsync<VocabNotFoundException>(
            () => handler.Handle(new ReviewVocabCommand(vocab.Id), default));
    }
}

public class ReviewVocabCommandValidatorTests
{
    private readonly ReviewVocabCommandValidator _validator = new();

    [Fact]
    public void Accepts_an_id()
    {
        Assert.True(_validator.Validate(new ReviewVocabCommand("id")).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_a_missing_id(string? id)
    {
        Assert.False(_validator.Validate(new ReviewVocabCommand(id!)).IsValid);
    }
}

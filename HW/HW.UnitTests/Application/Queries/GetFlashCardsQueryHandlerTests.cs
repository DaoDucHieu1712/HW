using HW.Application.Features.Vocabs.Queries.GetFlashCards;
using HW.Domain.Enums;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Queries;

public class GetFlashCardsQueryHandlerTests
{
    private static readonly DateTimeOffset Today = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Draws_from_everything_by_default()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("alpha").NextReviewAt(Today.AddDays(30)).Build(),
            VocabBuilder.A().WithWord("beta").NextReviewAt(Today.AddDays(-1)).Build());

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(null, null, UseDaily: false), default);

        Assert.Equal(2, deck.Count);
    }

    [Fact]
    public async Task Carries_the_word_its_meaning_and_its_stage_onto_each_card()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A()
            .WithWord("serendipity")
            .WithContent("(n) a happy accident")
            .AtStage(ReviewStage.Reviewed)
            .Build();
        await harness.SeedAsync(vocab);

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(null, null, UseDaily: false), default);

        var card = Assert.Single(deck);
        Assert.Equal(vocab.Id, card.VocabId);
        Assert.Equal("serendipity", card.Word);
        Assert.Equal("(n) a happy accident", card.Content);
        Assert.Equal((int)ReviewStage.Reviewed, card.ReviewStage);
    }

    [Fact]
    public async Task Draws_only_what_is_due_when_asked_for_a_review_session()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("due").NextReviewAt(Today.AddDays(-1)).Build(),
            VocabBuilder.A().WithWord("not-due").NextReviewAt(Today.AddDays(30)).Build(),
            VocabBuilder.A().WithWord("mastered").AtStage(ReviewStage.Mastered).NextReviewAt(Today.AddDays(-1)).Build());

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(null, null, UseDaily: true), default);

        Assert.Equal("due", Assert.Single(deck).Word);
    }

    [Fact]
    public async Task Drills_a_single_stage_when_one_is_named()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("new-word").Build(),
            VocabBuilder.A().WithWord("reviewed-word").AtStage(ReviewStage.Reviewed).Build(),
            VocabBuilder.A().WithWord("reinforced-word").AtStage(ReviewStage.Reinforced).Build());

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(null, (int)ReviewStage.Reviewed, UseDaily: false), default);

        Assert.Equal("reviewed-word", Assert.Single(deck).Word);
    }

    [Fact]
    public async Task Combines_the_due_filter_with_the_stage_filter()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("due-and-reviewed").AtStage(ReviewStage.Reviewed).NextReviewAt(Today.AddDays(-1)).Build(),
            VocabBuilder.A().WithWord("due-but-new").NextReviewAt(Today.AddDays(-1)).Build(),
            VocabBuilder.A().WithWord("reviewed-but-not-due").AtStage(ReviewStage.Reviewed).NextReviewAt(Today.AddDays(30)).Build());

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(null, (int)ReviewStage.Reviewed, UseDaily: true), default);

        Assert.Equal("due-and-reviewed", Assert.Single(deck).Word);
    }

    [Fact]
    public async Task Never_draws_more_cards_than_asked_for()
    {
        using var harness = new VocabTestHarness();
        var words = Enumerable.Range(1, 10)
            .Select(i => VocabBuilder.A().WithWord($"word-{i}").Build())
            .ToArray();
        await harness.SeedAsync(words);

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(3, null, UseDaily: false), default);

        Assert.Equal(3, deck.Count);
        Assert.Equal(3, deck.Select(c => c.VocabId).Distinct().Count());
    }

    [Fact]
    public async Task Asking_for_more_cards_than_exist_just_returns_what_there_is()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(VocabBuilder.A().Build(), VocabBuilder.A().WithWord("other").Build());

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(50, null, UseDaily: false), default);

        Assert.Equal(2, deck.Count);
    }

    [Fact]
    public async Task Leaves_soft_deleted_words_out_of_the_deck()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("kept").Build(),
            VocabBuilder.A().WithWord("deleted").SoftDeleted().Build());

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(null, null, UseDaily: false), default);

        Assert.Equal("kept", Assert.Single(deck).Word);
    }

    [Fact]
    public async Task Nothing_matching_is_an_empty_deck()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(VocabBuilder.A().Build());

        var deck = await new GetFlashCardsQueryHandler(harness.Repository)
            .Handle(new GetFlashCardsQuery(null, (int)ReviewStage.Mastered, UseDaily: false), default);

        Assert.Empty(deck);
    }
}

public class GetFlashCardsQueryValidatorTests
{
    private readonly GetFlashCardsQueryValidator _validator = new();

    [Fact]
    public void Accepts_an_omitted_count()
    {
        Assert.True(_validator.Validate(new GetFlashCardsQuery(null, null, false)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_deck_of_no_cards(int count)
    {
        Assert.False(_validator.Validate(new GetFlashCardsQuery(count, null, false)).IsValid);
    }

    [Fact]
    public void Accepts_a_positive_count()
    {
        Assert.True(_validator.Validate(new GetFlashCardsQuery(10, null, false)).IsValid);
    }
}

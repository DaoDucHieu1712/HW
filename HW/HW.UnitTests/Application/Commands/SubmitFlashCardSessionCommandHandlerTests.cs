using HW.Application.Features.Vocabs.Commands.SubmitFlashCardSession;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Enums;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Commands;

public class SubmitFlashCardSessionCommandHandlerTests
{
    private static VocabDtos.FlashCardResultRequestDto Knew(string id) => new(id, true);

    private static VocabDtos.FlashCardResultRequestDto Missed(string id) => new(id, false);

    [Fact]
    public async Task Counts_the_whole_session()
    {
        using var harness = new VocabTestHarness();
        var a = VocabBuilder.A().WithWord("a").Build();
        var b = VocabBuilder.A().WithWord("b").Build();
        var c = VocabBuilder.A().WithWord("c").Build();
        await harness.SeedAsync(a, b, c);

        var handler = new SubmitFlashCardSessionCommandHandler(harness.Repository);
        var result = await handler.Handle(
            new SubmitFlashCardSessionCommand([Knew(a.Id), Missed(b.Id), Knew(c.Id)]), default);

        Assert.Equal(3, result.TotalCards);
        Assert.Equal(2, result.KnewCount);
        Assert.Equal(1, result.DidntKnowCount);
    }

    [Fact]
    public async Task Advances_only_the_cards_the_user_knew()
    {
        using var harness = new VocabTestHarness();
        var known = VocabBuilder.A().WithWord("known").Build();
        var missed = VocabBuilder.A().WithWord("missed").Build();
        await harness.SeedAsync(known, missed);

        var handler = new SubmitFlashCardSessionCommandHandler(harness.Repository);
        var result = await handler.Handle(
            new SubmitFlashCardSessionCommand([Knew(known.Id), Missed(missed.Id)]), default);
        await harness.SaveAsync();

        Assert.Equal(new[] { known.Id }, result.AdvancedVocabIds);
        Assert.Equal(ReviewStage.Reviewed, (await harness.ReloadAsync(known.Id))!.ReviewStage);
        Assert.Equal(ReviewStage.New, (await harness.ReloadAsync(missed.Id))!.ReviewStage);
    }

    [Fact]
    public async Task Skips_a_word_that_is_already_mastered_instead_of_failing_the_session()
    {
        // MarkReviewed throws on a mastered word; the handler has to absorb that, or one stale card
        // in the deck would sink the whole session.
        using var harness = new VocabTestHarness();
        var mastered = VocabBuilder.A().WithWord("mastered").AtStage(ReviewStage.Mastered).Build();
        var fresh = VocabBuilder.A().WithWord("fresh").Build();
        await harness.SeedAsync(mastered, fresh);

        var handler = new SubmitFlashCardSessionCommandHandler(harness.Repository);
        var result = await handler.Handle(
            new SubmitFlashCardSessionCommand([Knew(mastered.Id), Knew(fresh.Id)]), default);
        await harness.SaveAsync();

        Assert.Equal(2, result.KnewCount);
        Assert.Equal(new[] { fresh.Id }, result.AdvancedVocabIds);
        Assert.Equal(ReviewStage.Mastered, (await harness.ReloadAsync(mastered.Id))!.ReviewStage);
    }

    [Fact]
    public async Task Skips_an_id_that_no_longer_exists()
    {
        using var harness = new VocabTestHarness();
        var fresh = VocabBuilder.A().Build();
        await harness.SeedAsync(fresh);

        var handler = new SubmitFlashCardSessionCommandHandler(harness.Repository);
        var result = await handler.Handle(
            new SubmitFlashCardSessionCommand([Knew("deleted-since-the-deck-was-drawn"), Knew(fresh.Id)]), default);

        Assert.Equal(2, result.TotalCards);
        Assert.Equal(2, result.KnewCount);
        Assert.Equal(new[] { fresh.Id }, result.AdvancedVocabIds);
    }

    [Fact]
    public async Task Does_not_read_the_words_the_user_missed()
    {
        using var harness = new VocabTestHarness();
        var missed = VocabBuilder.A().Build();
        await harness.SeedAsync(missed);

        var handler = new SubmitFlashCardSessionCommandHandler(harness.Repository);
        var result = await handler.Handle(
            new SubmitFlashCardSessionCommand([Missed(missed.Id), Missed("never-existed")]), default);

        Assert.Equal(0, result.KnewCount);
        Assert.Equal(2, result.DidntKnowCount);
        Assert.Empty(result.AdvancedVocabIds);
        Assert.Empty(harness.Db.ChangeTracker.Entries<HW.Domain.Entities.Vocab>());
    }

    [Fact]
    public async Task A_word_appearing_twice_advances_twice()
    {
        // Not a scenario the API should produce, but the handler loops over the results as given —
        // this pins that behavior down rather than leaving it to be discovered.
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().Build();
        await harness.SeedAsync(vocab);

        var handler = new SubmitFlashCardSessionCommandHandler(harness.Repository);
        var result = await handler.Handle(
            new SubmitFlashCardSessionCommand([Knew(vocab.Id), Knew(vocab.Id)]), default);
        await harness.SaveAsync();

        Assert.Equal(new[] { vocab.Id, vocab.Id }, result.AdvancedVocabIds);
        Assert.Equal(ReviewStage.Reinforced, (await harness.ReloadAsync(vocab.Id))!.ReviewStage);
    }
}

public class SubmitFlashCardSessionCommandValidatorTests
{
    private readonly SubmitFlashCardSessionCommandValidator _validator = new();

    [Fact]
    public void Accepts_a_session_with_results()
    {
        var command = new SubmitFlashCardSessionCommand([new VocabDtos.FlashCardResultRequestDto("id", true)]);

        Assert.True(_validator.Validate(command).IsValid);
    }

    [Fact]
    public void Rejects_an_empty_session()
    {
        Assert.False(_validator.Validate(new SubmitFlashCardSessionCommand([])).IsValid);
    }

    [Fact]
    public void Rejects_a_result_without_a_vocab_id()
    {
        var command = new SubmitFlashCardSessionCommand([new VocabDtos.FlashCardResultRequestDto("", true)]);

        Assert.False(_validator.Validate(command).IsValid);
    }
}

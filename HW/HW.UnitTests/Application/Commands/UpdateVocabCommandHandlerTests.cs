using HW.Application.Features.Vocabs.Commands.UpdateVocab;
using HW.Domain.Enums;
using HW.Domain.Exceptions;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Commands;

public class UpdateVocabCommandHandlerTests
{
    [Fact]
    public async Task Writes_both_fields_when_both_are_supplied()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithWord("recieve").WithContent("old meaning").Build();
        await harness.SeedAsync(vocab);

        var handler = new UpdateVocabCommandHandler(harness.Repository);
        await handler.Handle(new UpdateVocabCommand(vocab.Id, "receive", "(v) nhận"), default);
        await harness.SaveAsync();

        var updated = await harness.ReloadAsync(vocab.Id);
        Assert.NotNull(updated);
        Assert.Equal("receive", updated!.Word.Value);
        Assert.Equal("(v) nhận", updated.Content);
    }

    [Fact]
    public async Task Leaves_the_word_alone_when_only_the_meaning_is_supplied()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithWord("ephemeral").WithContent("old meaning").Build();
        await harness.SeedAsync(vocab);

        var handler = new UpdateVocabCommandHandler(harness.Repository);
        await handler.Handle(new UpdateVocabCommand(vocab.Id, null, "(adj) phù du"), default);
        await harness.SaveAsync();

        var updated = await harness.ReloadAsync(vocab.Id);
        Assert.Equal("ephemeral", updated!.Word.Value);
        Assert.Equal("(adj) phù du", updated.Content);
    }

    [Fact]
    public async Task Leaves_the_meaning_alone_when_only_the_word_is_supplied()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithWord("recieve").WithContent("(v) nhận").Build();
        await harness.SeedAsync(vocab);

        var handler = new UpdateVocabCommandHandler(harness.Repository);
        await handler.Handle(new UpdateVocabCommand(vocab.Id, "receive", null), default);
        await harness.SaveAsync();

        var updated = await harness.ReloadAsync(vocab.Id);
        Assert.Equal("receive", updated!.Word.Value);
        Assert.Equal("(v) nhận", updated.Content);
    }

    [Fact]
    public async Task Trims_the_new_word()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().Build();
        await harness.SeedAsync(vocab);

        var handler = new UpdateVocabCommandHandler(harness.Repository);
        await handler.Handle(new UpdateVocabCommand(vocab.Id, "  receive  ", null), default);
        await harness.SaveAsync();

        Assert.Equal("receive", (await harness.ReloadAsync(vocab.Id))!.Word.Value);
    }

    [Fact]
    public async Task Does_not_disturb_the_review_schedule()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().AtStage(ReviewStage.Reinforced).Build();
        var stage = vocab.ReviewStage;
        var due = vocab.NextReviewAt;
        await harness.SeedAsync(vocab);

        var handler = new UpdateVocabCommandHandler(harness.Repository);
        await handler.Handle(new UpdateVocabCommand(vocab.Id, "rewritten", "rewritten"), default);
        await harness.SaveAsync();

        var updated = await harness.ReloadAsync(vocab.Id);
        Assert.Equal(stage, updated!.ReviewStage);
        Assert.Equal(due, updated.NextReviewAt);
    }

    [Fact]
    public async Task Reports_an_unknown_id_as_not_found()
    {
        using var harness = new VocabTestHarness();
        var handler = new UpdateVocabCommandHandler(harness.Repository);

        var error = await Assert.ThrowsAsync<VocabNotFoundException>(
            () => handler.Handle(new UpdateVocabCommand("missing-id", "receive", null), default));

        Assert.Contains("missing-id", error.Message);
    }

    [Fact]
    public async Task Treats_a_soft_deleted_word_as_gone()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().SoftDeleted().Build();
        await harness.SeedAsync(vocab);

        var handler = new UpdateVocabCommandHandler(harness.Repository);

        await Assert.ThrowsAsync<VocabNotFoundException>(
            () => handler.Handle(new UpdateVocabCommand(vocab.Id, "receive", null), default));
    }

    [Fact]
    public async Task Refuses_a_replacement_word_the_value_object_rejects()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().Build();
        await harness.SeedAsync(vocab);

        var handler = new UpdateVocabCommandHandler(harness.Repository);

        await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(new UpdateVocabCommand(vocab.Id, new string('a', 201), null), default));
    }
}

public class UpdateVocabCommandValidatorTests
{
    private readonly UpdateVocabCommandValidator _validator = new();

    [Fact]
    public void Accepts_an_edit_to_the_meaning_only()
    {
        Assert.True(_validator.Validate(new UpdateVocabCommand("id", null, "new meaning")).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_a_missing_id(string? id)
    {
        var result = _validator.Validate(new UpdateVocabCommand(id!, "receive", null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVocabCommand.Id));
    }

    [Fact]
    public void Rejects_a_word_over_two_hundred_characters()
    {
        var result = _validator.Validate(new UpdateVocabCommand("id", new string('a', 201), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVocabCommand.Word));
    }

    [Fact]
    public void Does_not_apply_the_length_rule_to_an_omitted_word()
    {
        Assert.True(_validator.Validate(new UpdateVocabCommand("id", null, null)).IsValid);
    }
}

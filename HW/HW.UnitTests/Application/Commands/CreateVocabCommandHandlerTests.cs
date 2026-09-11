using HW.Application.Features.Vocabs.Commands.CreateVocab;
using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.Events.Vocabs;
using HW.Domain.Exceptions;
using HW.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HW.UnitTests.Application.Commands;

public class CreateVocabCommandHandlerTests
{
    [Fact]
    public async Task Stages_the_new_word_for_the_unit_of_work_to_commit()
    {
        using var harness = new VocabTestHarness();
        var handler = new CreateVocabCommandHandler(harness.Repository);

        await handler.Handle(new CreateVocabCommand("serendipity", "a happy accident"), default);

        // The handler adds but does not save — committing is the unit of work's job.
        Assert.Empty(await harness.Db.Vocabs.AsNoTracking().ToListAsync());

        await harness.SaveAsync();

        var saved = Assert.Single(await harness.Db.Vocabs.AsNoTracking().ToListAsync());
        Assert.Equal("serendipity", saved.Word.Value);
        Assert.Equal("a happy accident", saved.Content);
        Assert.Equal(ReviewStage.New, saved.ReviewStage);
    }

    [Fact]
    public async Task Trims_the_word_on_the_way_in()
    {
        using var harness = new VocabTestHarness();
        var handler = new CreateVocabCommandHandler(harness.Repository);

        await handler.Handle(new CreateVocabCommand("   serendipity  ", null), default);
        await harness.SaveAsync();

        var saved = Assert.Single(await harness.Db.Vocabs.AsNoTracking().ToListAsync());
        Assert.Equal("serendipity", saved.Word.Value);
    }

    [Fact]
    public async Task Raises_the_created_event_on_the_new_word()
    {
        using var harness = new VocabTestHarness();
        var handler = new CreateVocabCommandHandler(harness.Repository);

        await handler.Handle(new CreateVocabCommand("serendipity", null), default);

        var tracked = Assert.Single(harness.Db.ChangeTracker.Entries<Vocab>()).Entity;
        var raised = Assert.IsType<VocabCreatedDomainEvent>(Assert.Single(tracked.DomainEvents));
        Assert.Equal("serendipity", raised.Word);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Refuses_a_word_that_is_not_there(string? word)
    {
        using var harness = new VocabTestHarness();
        var handler = new CreateVocabCommandHandler(harness.Repository);

        await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(new CreateVocabCommand(word, "meaning"), default));

        Assert.Empty(harness.Db.ChangeTracker.Entries<Vocab>());
    }

    [Fact]
    public async Task Refuses_a_word_longer_than_the_value_object_allows()
    {
        using var harness = new VocabTestHarness();
        var handler = new CreateVocabCommandHandler(harness.Repository);

        await Assert.ThrowsAsync<BadRequestException>(
            () => handler.Handle(new CreateVocabCommand(new string('a', 201), null), default));
    }

    [Fact]
    public async Task The_domain_allows_the_same_word_twice()
    {
        // Nothing here enforces uniqueness — the agent's save tool is what refuses a duplicate, and
        // this test is what says that responsibility has not silently moved.
        using var harness = new VocabTestHarness();
        var handler = new CreateVocabCommandHandler(harness.Repository);

        await handler.Handle(new CreateVocabCommand("serendipity", "first"), default);
        await handler.Handle(new CreateVocabCommand("serendipity", "second"), default);
        await harness.SaveAsync();

        Assert.Equal(2, await harness.Db.Vocabs.CountAsync());
    }
}

public class CreateVocabCommandValidatorTests
{
    private readonly CreateVocabCommandValidator _validator = new();

    [Fact]
    public void Accepts_a_word_with_a_meaning()
    {
        Assert.True(_validator.Validate(new CreateVocabCommand("serendipity", "a happy accident")).IsValid);
    }

    [Fact]
    public void Accepts_a_word_without_a_meaning()
    {
        Assert.True(_validator.Validate(new CreateVocabCommand("serendipity", null)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_a_missing_word(string? word)
    {
        var result = _validator.Validate(new CreateVocabCommand(word, "meaning"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVocabCommand.Word));
    }

    [Fact]
    public void Rejects_a_word_over_two_hundred_characters()
    {
        var result = _validator.Validate(new CreateVocabCommand(new string('a', 201), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVocabCommand.Word));
    }

    [Fact]
    public void Accepts_a_word_of_exactly_two_hundred_characters()
    {
        Assert.True(_validator.Validate(new CreateVocabCommand(new string('a', 200), null)).IsValid);
    }
}

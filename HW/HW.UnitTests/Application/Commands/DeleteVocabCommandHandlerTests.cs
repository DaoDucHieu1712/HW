using HW.Application.Features.Vocabs.Commands.DeleteVocab;
using HW.Domain.Events.Vocabs;
using HW.Domain.Exceptions;
using HW.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HW.UnitTests.Application.Commands;

public class DeleteVocabCommandHandlerTests
{
    [Fact]
    public async Task Marks_the_word_deleted_rather_than_removing_the_row()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().Build();
        await harness.SeedAsync(vocab);

        var handler = new DeleteVocabCommandHandler(harness.Repository);
        await handler.Handle(new DeleteVocabCommand(vocab.Id), default);
        await harness.SaveAsync();

        Assert.Null(await harness.ReloadAsync(vocab.Id));

        var stillThere = await harness.ReloadIgnoringFiltersAsync(vocab.Id);
        Assert.NotNull(stillThere);
        Assert.True(stillThere!.IsDelete);
    }

    [Fact]
    public async Task Raises_the_deleted_event()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().Build();
        await harness.SeedAsync(vocab);

        var handler = new DeleteVocabCommandHandler(harness.Repository);
        await handler.Handle(new DeleteVocabCommand(vocab.Id), default);

        var tracked = Assert.Single(harness.Db.ChangeTracker.Entries<HW.Domain.Entities.Vocab>()).Entity;
        var raised = Assert.IsType<VocabDeletedDomainEvent>(Assert.Single(tracked.DomainEvents));
        Assert.Equal(vocab.Id, raised.VocabId);
    }

    [Fact]
    public async Task Leaves_the_other_words_alone()
    {
        using var harness = new VocabTestHarness();
        var target = VocabBuilder.A().WithWord("doomed").Build();
        var keeper = VocabBuilder.A().WithWord("spared").Build();
        await harness.SeedAsync(target, keeper);

        var handler = new DeleteVocabCommandHandler(harness.Repository);
        await handler.Handle(new DeleteVocabCommand(target.Id), default);
        await harness.SaveAsync();

        var remaining = Assert.Single(await harness.Db.Vocabs.AsNoTracking().ToListAsync());
        Assert.Equal("spared", remaining.Word.Value);
    }

    [Fact]
    public async Task Reports_an_unknown_id_as_not_found()
    {
        using var harness = new VocabTestHarness();
        var handler = new DeleteVocabCommandHandler(harness.Repository);

        var error = await Assert.ThrowsAsync<VocabNotFoundException>(
            () => handler.Handle(new DeleteVocabCommand("missing-id"), default));

        Assert.Contains("missing-id", error.Message);
    }

    [Fact]
    public async Task Deleting_a_word_twice_reports_it_as_not_found()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().SoftDeleted().Build();
        await harness.SeedAsync(vocab);

        var handler = new DeleteVocabCommandHandler(harness.Repository);

        await Assert.ThrowsAsync<VocabNotFoundException>(
            () => handler.Handle(new DeleteVocabCommand(vocab.Id), default));
    }
}

public class DeleteVocabCommandValidatorTests
{
    private readonly DeleteVocabCommandValidator _validator = new();

    [Fact]
    public void Accepts_an_id()
    {
        Assert.True(_validator.Validate(new DeleteVocabCommand("id")).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_a_missing_id(string? id)
    {
        Assert.False(_validator.Validate(new DeleteVocabCommand(id!)).IsValid);
    }
}

using HW.Application.Features.Vocabs.Queries.GetVocabById;
using HW.Domain.Enums;
using HW.Domain.Exceptions;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Queries;

public class GetVocabByIdQueryHandlerTests
{
    [Fact]
    public async Task Returns_the_word_in_full()
    {
        var notedAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A()
            .WithWord("serendipity")
            .WithContent("(n) a happy accident")
            .NotedAt(notedAt)
            .AtStage(ReviewStage.Reinforced)
            .Build();
        await harness.SeedAsync(vocab);

        var dto = await new GetVocabByIdQueryHandler(harness.Repository)
            .Handle(new GetVocabByIdQuery(vocab.Id), default);

        Assert.Equal(vocab.Id, dto.Id);
        Assert.Equal("serendipity", dto.Word);
        Assert.Equal("(n) a happy accident", dto.Content);
        Assert.Equal(notedAt, dto.NotedAt);
        Assert.Equal((int)ReviewStage.Reinforced, dto.ReviewStage);
        Assert.Equal(notedAt.AddDays(14), dto.NextReviewAt);
        Assert.NotNull(dto.LastReviewedAt);
        Assert.False(dto.IsCompleted);
    }

    [Fact]
    public async Task Reports_a_mastered_word_as_completed_with_nothing_scheduled()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().AtStage(ReviewStage.Mastered).Build();
        await harness.SeedAsync(vocab);

        var dto = await new GetVocabByIdQueryHandler(harness.Repository)
            .Handle(new GetVocabByIdQuery(vocab.Id), default);

        Assert.True(dto.IsCompleted);
        Assert.Null(dto.NextReviewAt);
    }

    [Fact]
    public async Task Reports_an_unknown_id_as_not_found()
    {
        using var harness = new VocabTestHarness();

        var error = await Assert.ThrowsAsync<VocabNotFoundException>(
            () => new GetVocabByIdQueryHandler(harness.Repository)
                .Handle(new GetVocabByIdQuery("missing-id"), default));

        Assert.Contains("missing-id", error.Message);
    }

    [Fact]
    public async Task A_soft_deleted_word_is_not_found()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().SoftDeleted().Build();
        await harness.SeedAsync(vocab);

        await Assert.ThrowsAsync<VocabNotFoundException>(
            () => new GetVocabByIdQueryHandler(harness.Repository)
                .Handle(new GetVocabByIdQuery(vocab.Id), default));
    }
}

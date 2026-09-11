using HW.Application.Features.Vocabs.Queries.GetVocabs;
using HW.Domain.Enums;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Queries;

public class GetVocabsQueryHandlerTests
{
    private static GetVocabsQuery All(int pageIndex = 1, int pageSize = 50)
        => new(null, null, null, pageIndex, pageSize);

    [Fact]
    public async Task Returns_every_word_when_nothing_is_filtered()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("alpha").Build(),
            VocabBuilder.A().WithWord("beta").Build());

        var result = await new GetVocabsQueryHandler(harness.Repository).Handle(All(), default);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Maps_the_word_and_its_schedule_onto_the_response()
    {
        var notedAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A()
                .WithWord("serendipity")
                .WithContent("(n) a happy accident")
                .NotedAt(notedAt)
                .AtStage(ReviewStage.Reviewed)
                .Build());

        var result = await new GetVocabsQueryHandler(harness.Repository).Handle(All(), default);

        var dto = Assert.Single(result.Items);
        Assert.Equal("serendipity", dto.Word);
        Assert.Equal("(n) a happy accident", dto.Content);
        Assert.Equal(notedAt, dto.NotedAt);
        Assert.Equal((int)ReviewStage.Reviewed, dto.ReviewStage);
        Assert.Equal(notedAt.AddDays(7), dto.NextReviewAt);
        Assert.False(dto.IsCompleted);
    }

    [Fact]
    public async Task Hides_soft_deleted_words()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("visible").Build(),
            VocabBuilder.A().WithWord("gone").SoftDeleted().Build());

        var result = await new GetVocabsQueryHandler(harness.Repository).Handle(All(), default);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("visible", Assert.Single(result.Items).Word);
    }

    [Theory]
    [InlineData("seren")]
    [InlineData("SEREN")]
    [InlineData("dipity")]
    [InlineData("SeReNdIpItY")]
    public async Task Search_matches_a_substring_of_the_word_regardless_of_case(string search)
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("Serendipity").Build(),
            VocabBuilder.A().WithWord("ephemeral").Build());

        var result = await new GetVocabsQueryHandler(harness.Repository)
            .Handle(new GetVocabsQuery(search, null, null, 1, 50), default);

        Assert.Equal("Serendipity", Assert.Single(result.Items).Word);
    }

    [Fact]
    public async Task Search_does_not_look_inside_the_meaning()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("ephemeral").WithContent("a happy accident").Build());

        var result = await new GetVocabsQueryHandler(harness.Repository)
            .Handle(new GetVocabsQuery("happy accident", null, null, 1, 50), default);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task A_blank_search_is_no_search_at_all()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(VocabBuilder.A().Build(), VocabBuilder.A().WithWord("other").Build());

        var result = await new GetVocabsQueryHandler(harness.Repository)
            .Handle(new GetVocabsQuery("   ", null, null, 1, 50), default);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Filters_on_the_date_the_word_was_noted()
    {
        var january = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var february = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);
        var march = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("january").NotedAt(january).Build(),
            VocabBuilder.A().WithWord("february").NotedAt(february).Build(),
            VocabBuilder.A().WithWord("march").NotedAt(march).Build());

        var handler = new GetVocabsQueryHandler(harness.Repository);

        var fromFebruary = await handler.Handle(new GetVocabsQuery(null, february, null, 1, 50), default);
        Assert.Equal(new[] { "march", "february" }, fromFebruary.Items.Select(x => x.Word));

        var untilFebruary = await handler.Handle(new GetVocabsQuery(null, null, february, 1, 50), default);
        Assert.Equal(new[] { "february", "january" }, untilFebruary.Items.Select(x => x.Word));

        var februaryOnly = await handler.Handle(new GetVocabsQuery(null, february, february, 1, 50), default);
        Assert.Equal("february", Assert.Single(februaryOnly.Items).Word);
    }

    [Fact]
    public async Task The_date_bounds_are_inclusive()
    {
        var noted = new DateTimeOffset(2026, 2, 15, 9, 30, 0, TimeSpan.Zero);
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(VocabBuilder.A().NotedAt(noted).Build());

        var handler = new GetVocabsQueryHandler(harness.Repository);

        Assert.Single((await handler.Handle(new GetVocabsQuery(null, noted, noted, 1, 50), default)).Items);
    }

    [Fact]
    public async Task Newest_first()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("oldest").CreatedAt(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Build(),
            VocabBuilder.A().WithWord("newest").CreatedAt(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)).Build(),
            VocabBuilder.A().WithWord("middle").CreatedAt(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)).Build());

        var result = await new GetVocabsQueryHandler(harness.Repository).Handle(All(), default);

        Assert.Equal(new[] { "newest", "middle", "oldest" }, result.Items.Select(x => x.Word));
    }

    [Fact]
    public async Task Pages_through_the_list()
    {
        using var harness = new VocabTestHarness();
        var words = Enumerable.Range(1, 5)
            .Select(i => VocabBuilder.A()
                .WithWord($"word-{i}")
                .CreatedAt(new DateTimeOffset(2026, 1, i, 0, 0, 0, TimeSpan.Zero))
                .Build())
            .ToArray();
        await harness.SeedAsync(words);

        var handler = new GetVocabsQueryHandler(harness.Repository);

        var first = await handler.Handle(All(pageIndex: 1, pageSize: 2), default);
        Assert.Equal(new[] { "word-5", "word-4" }, first.Items.Select(x => x.Word));
        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.True(first.HasNextPage);
        Assert.False(first.HasPreviousPage);

        var last = await handler.Handle(All(pageIndex: 3, pageSize: 2), default);
        Assert.Equal(new[] { "word-1" }, last.Items.Select(x => x.Word));
        Assert.False(last.HasNextPage);
        Assert.True(last.HasPreviousPage);
    }

    [Fact]
    public async Task A_page_beyond_the_end_is_empty_but_still_reports_the_total()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(VocabBuilder.A().Build());

        var result = await new GetVocabsQueryHandler(harness.Repository)
            .Handle(All(pageIndex: 10, pageSize: 10), default);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task A_nonsense_page_falls_back_to_the_defaults()
    {
        using var harness = new VocabTestHarness();
        var words = Enumerable.Range(1, 12)
            .Select(i => VocabBuilder.A()
                .WithWord($"word-{i:00}")
                .CreatedAt(new DateTimeOffset(2026, 1, i, 0, 0, 0, TimeSpan.Zero))
                .Build())
            .ToArray();
        await harness.SeedAsync(words);

        var result = await new GetVocabsQueryHandler(harness.Repository)
            .Handle(All(pageIndex: 0, pageSize: 0), default);

        Assert.Equal(1, result.PageIndex);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public async Task A_page_size_over_the_ceiling_is_clamped()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(VocabBuilder.A().Build());

        var result = await new GetVocabsQueryHandler(harness.Repository)
            .Handle(All(pageIndex: 1, pageSize: 5_000), default);

        Assert.Equal(100, result.PageSize);
    }
}

using HW.Application.Features.Vocabs.Queries.GetDailyMission;
using HW.Domain.Enums;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Queries;

public class GetDailyMissionQueryHandlerTests
{
    private static readonly DateTimeOffset Today = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Returns_the_words_that_have_come_due()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("overdue").NextReviewAt(Today.AddDays(-5)).Build(),
            VocabBuilder.A().WithWord("due-today").NextReviewAt(Today).Build(),
            VocabBuilder.A().WithWord("not-yet").NextReviewAt(Today.AddDays(5)).Build());

        var due = await new GetDailyMissionQueryHandler(harness.Repository)
            .Handle(new GetDailyMissionQuery(), default);

        Assert.Equal(new[] { "overdue", "due-today" }, due.Select(x => x.Word));
    }

    [Fact]
    public async Task Counts_anything_falling_before_tomorrow_as_due_today()
    {
        // The cut-off is the start of tomorrow, so a word scheduled for later today still counts.
        var endOfToday = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddHours(23).AddMinutes(59);

        using var harness = new VocabTestHarness();
        await harness.SeedAsync(VocabBuilder.A().WithWord("later-today").NextReviewAt(endOfToday).Build());

        var due = await new GetDailyMissionQueryHandler(harness.Repository)
            .Handle(new GetDailyMissionQuery(), default);

        Assert.Equal("later-today", Assert.Single(due).Word);
    }

    [Fact]
    public async Task A_word_due_at_midnight_tomorrow_is_not_in_today_s_mission()
    {
        var startOfTomorrow = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(1), TimeSpan.Zero);

        using var harness = new VocabTestHarness();
        await harness.SeedAsync(VocabBuilder.A().NextReviewAt(startOfTomorrow).Build());

        var due = await new GetDailyMissionQueryHandler(harness.Repository)
            .Handle(new GetDailyMissionQuery(), default);

        Assert.Empty(due);
    }

    [Fact]
    public async Task Mastered_words_never_come_back()
    {
        // A mastered word has no next review date of its own, so the stage check is what does the
        // work here — this seeds one with a due date anyway to prove the check is real.
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A()
                .WithWord("mastered")
                .AtStage(ReviewStage.Mastered)
                .NextReviewAt(Today.AddDays(-1))
                .Build());

        var due = await new GetDailyMissionQueryHandler(harness.Repository)
            .Handle(new GetDailyMissionQuery(), default);

        Assert.Empty(due);
    }

    [Fact]
    public async Task Soft_deleted_words_never_come_back_either()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("deleted").NextReviewAt(Today.AddDays(-1)).SoftDeleted().Build());

        var due = await new GetDailyMissionQueryHandler(harness.Repository)
            .Handle(new GetDailyMissionQuery(), default);

        Assert.Empty(due);
    }

    [Fact]
    public async Task The_longest_overdue_word_comes_first()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("two-days-late").NextReviewAt(Today.AddDays(-2)).Build(),
            VocabBuilder.A().WithWord("ten-days-late").NextReviewAt(Today.AddDays(-10)).Build(),
            VocabBuilder.A().WithWord("five-days-late").NextReviewAt(Today.AddDays(-5)).Build());

        var due = await new GetDailyMissionQueryHandler(harness.Repository)
            .Handle(new GetDailyMissionQuery(), default);

        Assert.Equal(new[] { "ten-days-late", "five-days-late", "two-days-late" }, due.Select(x => x.Word));
    }

    [Fact]
    public async Task Nothing_due_is_an_empty_list_not_an_error()
    {
        using var harness = new VocabTestHarness();

        var due = await new GetDailyMissionQueryHandler(harness.Repository)
            .Handle(new GetDailyMissionQuery(), default);

        Assert.Empty(due);
    }

    [Fact]
    public async Task Carries_the_review_stage_through_to_the_response()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().AtStage(ReviewStage.Reviewed).NextReviewAt(Today.AddDays(-1)).Build());

        var due = await new GetDailyMissionQueryHandler(harness.Repository)
            .Handle(new GetDailyMissionQuery(), default);

        Assert.Equal((int)ReviewStage.Reviewed, Assert.Single(due).ReviewStage);
    }
}

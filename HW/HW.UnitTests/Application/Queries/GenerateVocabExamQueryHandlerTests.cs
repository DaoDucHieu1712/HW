using HW.Application.Features.Vocabs.Dtos;
using HW.Application.Features.Vocabs.Queries.GenerateVocabExam;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Queries;

/// <summary>
/// The generator picks question types, distractors, and the pool itself at random, so these tests
/// assert the invariants that must hold for every draw rather than a fixed exam.
/// </summary>
public class GenerateVocabExamQueryHandlerTests
{
    private const int MultipleChoice = 0;
    private const int TrueFalse = 1;
    private const int Written = 2;

    private static async Task<VocabTestHarness> SeededAsync(int count)
    {
        var harness = new VocabTestHarness();
        var words = Enumerable.Range(1, count)
            .Select(i => VocabBuilder.A().WithWord($"word-{i:00}").WithContent($"meaning-{i:00}").Build())
            .ToArray();

        await harness.SeedAsync(words);
        return harness;
    }

    [Fact]
    public async Task Refuses_to_build_an_exam_with_no_words_to_draw_on()
    {
        using var harness = new VocabTestHarness();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GenerateVocabExamQueryHandler(harness.Repository)
                .Handle(new GenerateVocabExamQuery(5, null, null), default));

        Assert.Equal("No vocab words found.", error.Message);
    }

    [Fact]
    public async Task Builds_as_many_questions_as_asked_for()
    {
        using var harness = await SeededAsync(20);

        var questions = await new GenerateVocabExamQueryHandler(harness.Repository)
            .Handle(new GenerateVocabExamQuery(7, null, null), default);

        Assert.Equal(7, questions.Count);
    }

    [Fact]
    public async Task Asks_about_each_word_at_most_once()
    {
        using var harness = await SeededAsync(10);

        var questions = await new GenerateVocabExamQueryHandler(harness.Repository)
            .Handle(new GenerateVocabExamQuery(10, null, null), default);

        Assert.Equal(10, questions.Select(q => q.VocabId).Distinct().Count());
        Assert.Equal(10, questions.Select(q => q.QuestionId).Distinct().Count());
    }

    [Fact]
    public async Task Asking_for_more_questions_than_there_are_words_uses_every_word()
    {
        using var harness = await SeededAsync(3);

        var questions = await new GenerateVocabExamQueryHandler(harness.Repository)
            .Handle(new GenerateVocabExamQuery(50, null, null), default);

        Assert.Equal(3, questions.Count);
    }

    [Fact]
    public async Task Every_question_is_one_of_the_three_supported_shapes()
    {
        using var harness = await SeededAsync(25);

        var questions = await new GenerateVocabExamQueryHandler(harness.Repository)
            .Handle(new GenerateVocabExamQuery(25, null, null), default);

        Assert.All(questions, q => Assert.InRange(q.Type, MultipleChoice, Written));
    }

    [Fact]
    public async Task A_multiple_choice_question_offers_four_options_including_the_right_one()
    {
        using var harness = await SeededAsync(25);

        var questions = await ManyDrawsAsync(harness);

        var multipleChoice = questions.Where(q => q.Type == MultipleChoice).ToList();
        Assert.NotEmpty(multipleChoice);

        foreach (var question in multipleChoice)
        {
            Assert.NotNull(question.Options);
            Assert.Equal(4, question.Options!.Count);
            Assert.Equal(4, question.Options.Distinct().Count());
            Assert.Contains(ExpectedMeaning(question.Word), question.Options);
            Assert.Null(question.DisplayedContent);
        }
    }

    [Fact]
    public async Task A_true_false_question_shows_a_meaning_to_judge()
    {
        using var harness = await SeededAsync(25);

        var questions = await ManyDrawsAsync(harness);

        var trueFalse = questions.Where(q => q.Type == TrueFalse).ToList();
        Assert.NotEmpty(trueFalse);

        foreach (var question in trueFalse)
        {
            Assert.False(string.IsNullOrWhiteSpace(question.DisplayedContent));
            Assert.Null(question.Options);
        }
    }

    [Fact]
    public async Task A_written_question_gives_nothing_away()
    {
        using var harness = await SeededAsync(25);

        var questions = await ManyDrawsAsync(harness);

        foreach (var question in questions.Where(q => q.Type == Written))
        {
            Assert.Null(question.Options);
            Assert.Null(question.DisplayedContent);
        }
    }

    [Fact]
    public async Task A_single_saved_word_still_produces_an_exam()
    {
        // With nothing to draw distractors from, multiple choice has one option and true/false can
        // only ever show the real meaning — degraded, but it must not throw.
        using var harness = await SeededAsync(1);

        var questions = await new GenerateVocabExamQueryHandler(harness.Repository)
            .Handle(new GenerateVocabExamQuery(1, null, null), default);

        var question = Assert.Single(questions);
        Assert.Equal("word-01", question.Word);

        if (question.Type == MultipleChoice)
            Assert.Equal(new[] { "meaning-01" }, question.Options);

        if (question.Type == TrueFalse)
            Assert.Equal("meaning-01", question.DisplayedContent);
    }

    [Fact]
    public async Task Draws_only_on_words_noted_in_the_requested_window()
    {
        var january = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var june = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("january").WithContent("winter").NotedAt(january).Build(),
            VocabBuilder.A().WithWord("june").WithContent("summer").NotedAt(june).Build());

        var handler = new GenerateVocabExamQueryHandler(harness.Repository);

        var fromMarch = await handler.Handle(
            new GenerateVocabExamQuery(10, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), null), default);
        Assert.Equal("june", Assert.Single(fromMarch).Word);

        var untilMarch = await handler.Handle(
            new GenerateVocabExamQuery(10, null, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)), default);
        Assert.Equal("january", Assert.Single(untilMarch).Word);
    }

    [Fact]
    public async Task An_empty_window_is_reported_as_having_no_words()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().NotedAt(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Build());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GenerateVocabExamQueryHandler(harness.Repository).Handle(
                new GenerateVocabExamQuery(
                    5,
                    new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    null),
                default));
    }

    [Fact]
    public async Task Leaves_soft_deleted_words_out_of_the_exam()
    {
        using var harness = new VocabTestHarness();
        await harness.SeedAsync(
            VocabBuilder.A().WithWord("kept").WithContent("kept meaning").Build(),
            VocabBuilder.A().WithWord("deleted").WithContent("deleted meaning").SoftDeleted().Build());

        var questions = await new GenerateVocabExamQueryHandler(harness.Repository)
            .Handle(new GenerateVocabExamQuery(10, null, null), default);

        var question = Assert.Single(questions);
        Assert.Equal("kept", question.Word);
        Assert.DoesNotContain("deleted meaning", question.Options ?? []);
        Assert.NotEqual("deleted meaning", question.DisplayedContent);
    }

    /// <summary>The seeded words pair "word-NN" with "meaning-NN", so the right answer is derivable.</summary>
    private static string ExpectedMeaning(string word) => word.Replace("word-", "meaning-");

    /// <summary>
    /// Question types are drawn at random, so a single exam is not guaranteed to contain one of each.
    /// Pooling several draws makes "there was at least one of this shape" a safe assertion rather
    /// than a coin flip, while still asserting the per-question invariants on every one drawn.
    /// </summary>
    private static async Task<List<VocabDtos.VocabExamQuestionDto>> ManyDrawsAsync(VocabTestHarness harness)
    {
        var handler = new GenerateVocabExamQueryHandler(harness.Repository);
        var questions = new List<VocabDtos.VocabExamQuestionDto>();

        for (var draw = 0; draw < 5; draw++)
            questions.AddRange(await handler.Handle(new GenerateVocabExamQuery(25, null, null), default));

        return questions;
    }
}

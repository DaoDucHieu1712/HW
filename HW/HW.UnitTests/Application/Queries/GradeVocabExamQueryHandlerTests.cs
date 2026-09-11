using HW.Application.Features.Vocabs.Dtos;
using HW.Application.Features.Vocabs.Queries.GradeVocabExam;
using HW.Domain.Entities;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Queries;

public class GradeVocabExamQueryHandlerTests
{
    private const int MultipleChoice = 0;
    private const int TrueFalse = 1;
    private const int Written = 2;

    private static VocabDtos.ExamAnswerDto Answer(
        Vocab vocab, int type, string answer, string? displayedContent = null)
        => new($"q-{vocab.Id}", vocab.Id, type, displayedContent, answer);

    [Theory]
    [InlineData(MultipleChoice)]
    [InlineData(Written)]
    public async Task Marks_an_answer_matching_the_meaning_correct(int type)
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithWord("serendipity").WithContent("a happy accident").Build();
        await harness.SeedAsync(vocab);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery([Answer(vocab, type, "a happy accident")]), default);

        var graded = Assert.Single(result.Results);
        Assert.True(graded.IsCorrect);
        Assert.Equal("serendipity", graded.Word);
        Assert.Equal("a happy accident", graded.CorrectAnswer);
        Assert.Equal("a happy accident", graded.SubmittedAnswer);
    }

    [Theory]
    [InlineData("A HAPPY ACCIDENT")]
    [InlineData("  a happy accident  ")]
    [InlineData("\tA Happy Accident\n")]
    public async Task Ignores_case_and_surrounding_whitespace(string submitted)
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithContent("a happy accident").Build();
        await harness.SeedAsync(vocab);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery([Answer(vocab, Written, submitted)]), default);

        Assert.True(Assert.Single(result.Results).IsCorrect);
        Assert.Equal(submitted.Trim(), Assert.Single(result.Results).SubmittedAnswer);
    }

    [Fact]
    public async Task Marks_a_wrong_meaning_wrong_and_says_what_was_expected()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithContent("a happy accident").Build();
        await harness.SeedAsync(vocab);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery([Answer(vocab, MultipleChoice, "an unhappy accident")]), default);

        var graded = Assert.Single(result.Results);
        Assert.False(graded.IsCorrect);
        Assert.Equal("a happy accident", graded.CorrectAnswer);
        Assert.Equal("an unhappy accident", graded.SubmittedAnswer);
    }

    [Fact]
    public async Task True_false_is_correct_when_the_shown_meaning_really_is_the_word_s()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithContent("a happy accident").Build();
        await harness.SeedAsync(vocab);

        var handler = new GradeVocabExamQueryHandler(harness.Repository);

        var saidTrue = await handler.Handle(
            new GradeVocabExamQuery([Answer(vocab, TrueFalse, "true", "a happy accident")]), default);
        Assert.True(Assert.Single(saidTrue.Results).IsCorrect);
        Assert.Equal("true", Assert.Single(saidTrue.Results).CorrectAnswer);

        var saidFalse = await handler.Handle(
            new GradeVocabExamQuery([Answer(vocab, TrueFalse, "false", "a happy accident")]), default);
        Assert.False(Assert.Single(saidFalse.Results).IsCorrect);
    }

    [Fact]
    public async Task True_false_is_correct_when_the_shown_meaning_belongs_to_another_word()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithContent("a happy accident").Build();
        await harness.SeedAsync(vocab);

        var handler = new GradeVocabExamQueryHandler(harness.Repository);

        var saidFalse = await handler.Handle(
            new GradeVocabExamQuery([Answer(vocab, TrueFalse, "false", "lasting a very short time")]), default);
        Assert.True(Assert.Single(saidFalse.Results).IsCorrect);
        Assert.Equal("false", Assert.Single(saidFalse.Results).CorrectAnswer);

        var saidTrue = await handler.Handle(
            new GradeVocabExamQuery([Answer(vocab, TrueFalse, "true", "lasting a very short time")]), default);
        Assert.False(Assert.Single(saidTrue.Results).IsCorrect);
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    public async Task True_false_accepts_the_answer_however_it_is_cased(string submitted)
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithContent("a happy accident").Build();
        await harness.SeedAsync(vocab);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery([Answer(vocab, TrueFalse, submitted, "a happy accident")]), default);

        Assert.True(Assert.Single(result.Results).IsCorrect);
    }

    [Fact]
    public async Task True_false_compares_the_shown_meaning_ignoring_case_and_padding()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithContent("a happy accident").Build();
        await harness.SeedAsync(vocab);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery([Answer(vocab, TrueFalse, "true", "  A Happy Accident  ")]), default);

        Assert.True(Assert.Single(result.Results).IsCorrect);
    }

    [Fact]
    public async Task Scores_the_paper_out_of_a_hundred()
    {
        using var harness = new VocabTestHarness();
        var right = VocabBuilder.A().WithWord("right").WithContent("correct meaning").Build();
        var wrong = VocabBuilder.A().WithWord("wrong").WithContent("correct meaning too").Build();
        await harness.SeedAsync(right, wrong);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery(
            [
                Answer(right, Written, "correct meaning"),
                Answer(wrong, Written, "no idea"),
            ]), default);

        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(1, result.CorrectCount);
        Assert.Equal(50d, result.Score);
    }

    [Fact]
    public async Task Rounds_the_score_to_two_decimal_places()
    {
        using var harness = new VocabTestHarness();
        var words = Enumerable.Range(1, 3)
            .Select(i => VocabBuilder.A().WithWord($"word-{i}").WithContent($"meaning-{i}").Build())
            .ToArray();
        await harness.SeedAsync(words);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery(
            [
                Answer(words[0], Written, "meaning-1"),
                Answer(words[1], Written, "nope"),
                Answer(words[2], Written, "nope"),
            ]), default);

        Assert.Equal(33.33d, result.Score);
    }

    [Fact]
    public async Task A_perfect_paper_scores_a_hundred()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithContent("a happy accident").Build();
        await harness.SeedAsync(vocab);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery([Answer(vocab, Written, "a happy accident")]), default);

        Assert.Equal(100d, result.Score);
    }

    [Fact]
    public async Task Keeps_the_answers_in_the_order_they_were_submitted()
    {
        using var harness = new VocabTestHarness();
        var first = VocabBuilder.A().WithWord("first").WithContent("one").Build();
        var second = VocabBuilder.A().WithWord("second").WithContent("two").Build();
        await harness.SeedAsync(first, second);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery(
            [
                Answer(second, Written, "two"),
                Answer(first, Written, "one"),
            ]), default);

        Assert.Equal(new[] { "second", "first" }, result.Results.Select(r => r.Word));
    }

    [Fact]
    public async Task An_answer_about_a_word_that_is_gone_falls_back_to_its_id()
    {
        // A word deleted between generating and submitting the exam still has to be reported on
        // rather than dropped, or the paper would grade out of the wrong total.
        using var harness = new VocabTestHarness();

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery(
                [new VocabDtos.ExamAnswerDto("q1", "vanished-id", Written, null, "anything")]), default);

        var graded = Assert.Single(result.Results);
        Assert.Equal("vanished-id", graded.Word);
        Assert.False(graded.IsCorrect);
        Assert.Equal(string.Empty, graded.CorrectAnswer);
        Assert.Equal(1, result.TotalQuestions);
        Assert.Equal(0, result.CorrectCount);
    }

    [Fact]
    public async Task A_soft_deleted_word_is_treated_the_same_way()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithWord("gone").WithContent("a happy accident").SoftDeleted().Build();
        await harness.SeedAsync(vocab);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery([Answer(vocab, Written, "a happy accident")]), default);

        var graded = Assert.Single(result.Results);
        Assert.Equal(vocab.Id, graded.Word);
        Assert.False(graded.IsCorrect);
    }

    [Fact]
    public async Task A_word_saved_without_a_meaning_can_only_be_answered_blank()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithContent(null).Build();
        await harness.SeedAsync(vocab);

        var handler = new GradeVocabExamQueryHandler(harness.Repository);

        var blank = await handler.Handle(new GradeVocabExamQuery([Answer(vocab, Written, "   ")]), default);
        Assert.True(Assert.Single(blank.Results).IsCorrect);

        var guessed = await handler.Handle(new GradeVocabExamQuery([Answer(vocab, Written, "a guess")]), default);
        Assert.False(Assert.Single(guessed.Results).IsCorrect);
    }

    [Fact]
    public async Task Grades_every_answer_when_two_questions_ask_about_the_same_word()
    {
        using var harness = new VocabTestHarness();
        var vocab = VocabBuilder.A().WithWord("repeated").WithContent("the meaning").Build();
        await harness.SeedAsync(vocab);

        var result = await new GradeVocabExamQueryHandler(harness.Repository)
            .Handle(new GradeVocabExamQuery(
            [
                new VocabDtos.ExamAnswerDto("q1", vocab.Id, Written, null, "the meaning"),
                new VocabDtos.ExamAnswerDto("q2", vocab.Id, MultipleChoice, null, "wrong"),
            ]), default);

        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(1, result.CorrectCount);
        Assert.Equal(new[] { "q1", "q2" }, result.Results.Select(r => r.QuestionId));
        Assert.All(result.Results, r => Assert.Equal("repeated", r.Word));
    }
}

public class GradeVocabExamQueryValidatorTests
{
    private readonly GradeVocabExamQueryValidator _validator = new();

    private static VocabDtos.ExamAnswerDto Valid() => new("q1", "v1", 2, null, "an answer");

    [Fact]
    public void Accepts_a_submitted_paper()
    {
        Assert.True(_validator.Validate(new GradeVocabExamQuery([Valid()])).IsValid);
    }

    [Fact]
    public void Rejects_a_paper_with_no_answers()
    {
        Assert.False(_validator.Validate(new GradeVocabExamQuery([])).IsValid);
    }

    [Fact]
    public void Rejects_an_answer_without_a_question_id()
    {
        var query = new GradeVocabExamQuery([Valid() with { QuestionId = "" }]);

        Assert.False(_validator.Validate(query).IsValid);
    }

    [Fact]
    public void Rejects_an_answer_without_a_vocab_id()
    {
        var query = new GradeVocabExamQuery([Valid() with { VocabId = "" }]);

        Assert.False(_validator.Validate(query).IsValid);
    }

    [Fact]
    public void Rejects_an_answer_that_was_never_filled_in()
    {
        var query = new GradeVocabExamQuery([Valid() with { Answer = null! }]);

        Assert.False(_validator.Validate(query).IsValid);
    }

    [Fact]
    public void Accepts_a_deliberately_blank_answer()
    {
        // NotNull, not NotEmpty: leaving a written question blank is an answer, and a wrong one.
        Assert.True(_validator.Validate(new GradeVocabExamQuery([Valid() with { Answer = "" }])).IsValid);
    }
}

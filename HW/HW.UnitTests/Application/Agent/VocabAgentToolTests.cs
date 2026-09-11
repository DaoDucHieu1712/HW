using HW.Agentic.Core;
using HW.Application.Features.Vocabs.Agent.Tools;
using HW.Application.Features.Vocabs.Commands.CreateVocab;
using HW.Application.Features.Vocabs.Commands.ReviewVocab;
using HW.Application.Features.Vocabs.Commands.UpdateVocab;
using HW.Application.Features.Vocabs.Dtos;
using HW.Application.Features.Vocabs.Queries.GenerateVocabExam;
using HW.Application.Features.Vocabs.Queries.GetDailyMission;
using HW.Application.Features.Vocabs.Queries.GetFlashCards;
using HW.Application.Features.Vocabs.Queries.GetVocabById;
using HW.Application.Features.Vocabs.Queries.GetVocabs;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Enums;
using HW.Domain.Exceptions;
using HW.UnitTests.TestSupport;
using MediatR;

namespace HW.UnitTests.Application.Agent;

public class SearchVocabToolTests
{
    private static PagedResult<VocabDtos.VocabResponseDto> Page(
        params VocabDtos.VocabResponseDto[] items)
        => new([.. items], 1, 20, items.Length);

    [Fact]
    public async Task Passes_the_search_and_the_dates_straight_through()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity")));
        var tool = new SearchVocabTool(sender, new AgentLoopOptions());

        await tool.ExecuteAsync(Json.Args("""
            { "search": "seren", "notedFrom": "2026-01-31", "notedTo": "2026-02-28" }
            """));

        var query = sender.OnlySent<GetVocabsQuery>();
        Assert.Equal("seren", query.Search);
        Assert.Equal(new DateTime(2026, 1, 31), query.FromDate!.Value.DateTime);
        Assert.Equal(new DateTime(2026, 2, 28), query.ToDate!.Value.DateTime);
    }

    [Fact]
    public async Task Browses_the_newest_entries_when_nothing_is_supplied()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity")));
        var tool = new SearchVocabTool(sender, new AgentLoopOptions());

        await tool.ExecuteAsync(Json.NoArgs());

        var query = sender.OnlySent<GetVocabsQuery>();
        Assert.Null(query.Search);
        Assert.Null(query.FromDate);
        Assert.Null(query.ToDate);
        Assert.Equal(1, query.PageIndex);
        Assert.Equal(20, query.PageSize);
    }

    [Fact]
    public async Task Clamps_a_page_size_the_model_asked_too_much_of()
    {
        var options = new AgentLoopOptions { MaxToolResultItems = 25 };
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity")));

        await new SearchVocabTool(sender, options).ExecuteAsync(Json.Args("""{ "pageSize": 500 }"""));

        Assert.Equal(25, sender.OnlySent<GetVocabsQuery>().PageSize);
    }

    [Theory]
    [InlineData("""{ "pageSize": 0 }""", 1)]
    [InlineData("""{ "pageSize": -5 }""", 1)]
    public async Task Clamps_a_page_size_below_one(string args, int expected)
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity")));

        await new SearchVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args(args));

        Assert.Equal(expected, sender.OnlySent<GetVocabsQuery>().PageSize);
    }

    [Fact]
    public async Task Never_asks_for_a_page_before_the_first()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity")));

        await new SearchVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args("""{ "page": 0 }"""));

        Assert.Equal(1, sender.OnlySent<GetVocabsQuery>().PageIndex);
    }

    [Fact]
    public async Task Reads_numbers_the_model_sent_as_strings()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity")));

        await new SearchVocabTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "page": "3", "pageSize": "5" }"""));

        var query = sender.OnlySent<GetVocabsQuery>();
        Assert.Equal(3, query.PageIndex);
        Assert.Equal(5, query.PageSize);
    }

    [Fact]
    public async Task Says_so_in_words_when_nothing_matches()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page());

        var output = await new SearchVocabTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "search": "nothing" }"""));

        Assert.Equal("No saved words match that search.", output);
    }

    [Fact]
    public async Task Returns_the_matches_with_the_paging_the_model_needs_to_go_further()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(
            new PagedResult<VocabDtos.VocabResponseDto>([Dto.Vocab("serendipity")], 1, 20, 40));

        var output = await new SearchVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs());

        using var json = Json.Parse(output);
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, json.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(40, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.True(json.RootElement.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal("serendipity", json.RootElement.GetProperty("words")[0].GetProperty("word").GetString());
    }

    [Fact]
    public async Task Ignores_a_blank_search_string()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity")));

        await new SearchVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args("""{ "search": "   " }"""));

        Assert.Null(sender.OnlySent<GetVocabsQuery>().Search);
    }

    [Fact]
    public async Task Ignores_a_date_the_model_made_up()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity")));

        await new SearchVocabTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "notedFrom": "last tuesday" }"""));

        Assert.Null(sender.OnlySent<GetVocabsQuery>().FromDate);
    }
}

public class GetVocabToolTests
{
    [Fact]
    public async Task Reads_the_word_by_id()
    {
        var sender = new StubSender().Responds<GetVocabByIdQuery>(Dto.Vocab("serendipity", id: "v1"));

        var output = await new GetVocabTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "id": "v1" }"""));

        Assert.Equal("v1", sender.OnlySent<GetVocabByIdQuery>().Id);

        using var json = Json.Parse(output);
        Assert.Equal("serendipity", json.RootElement.GetProperty("word").GetString());
    }

    [Fact]
    public async Task Refuses_to_guess_at_a_missing_id()
    {
        var sender = new StubSender();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => new GetVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs()));

        Assert.Contains("'id' is required", error.Message);
        Assert.Empty(sender.Sent);
    }
}

public class DailyMissionToolTests
{
    [Fact]
    public async Task Says_so_in_words_when_nothing_is_due()
    {
        var sender = new StubSender().Responds<GetDailyMissionQuery>(new List<VocabDtos.VocabResponseDto>());

        var output = await new DailyMissionTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs());

        Assert.Equal("Nothing is due for review today.", output);
    }

    [Fact]
    public async Task Returns_the_queue_when_there_is_one()
    {
        var sender = new StubSender().Responds<GetDailyMissionQuery>(
            new List<VocabDtos.VocabResponseDto> { Dto.Vocab("serendipity"), Dto.Vocab("ephemeral") });

        var output = await new DailyMissionTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs());

        using var json = Json.Parse(output);
        Assert.Equal(2, json.RootElement.GetProperty("dueCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("shownCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("words").GetArrayLength());
    }

    [Fact]
    public async Task Truncates_a_queue_too_long_for_one_turn_and_says_how_long_it_really_is()
    {
        var options = new AgentLoopOptions { MaxToolResultItems = 3 };
        var due = Enumerable.Range(1, 50).Select(i => Dto.Vocab($"word-{i}")).ToList();
        var sender = new StubSender().Responds<GetDailyMissionQuery>(due);

        var output = await new DailyMissionTool(sender, options).ExecuteAsync(Json.NoArgs());

        using var json = Json.Parse(output);
        Assert.Equal(50, json.RootElement.GetProperty("dueCount").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("shownCount").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("words").GetArrayLength());
    }
}

public class FlashCardsToolTests
{
    private static List<VocabDtos.FlashCardDto> Deck(int count) =>
        Enumerable.Range(1, count).Select(i => new VocabDtos.FlashCardDto($"v{i}", $"word-{i}", "meaning", 0)).ToList();

    [Fact]
    public async Task Draws_ten_cards_from_everything_by_default()
    {
        var sender = new StubSender().Responds<GetFlashCardsQuery>(Deck(10));

        await new FlashCardsTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs());

        var query = sender.OnlySent<GetFlashCardsQuery>();
        Assert.Equal(10, query.Count);
        Assert.Null(query.ReviewStage);
        Assert.False(query.UseDaily);
    }

    [Fact]
    public async Task Passes_the_filters_the_model_chose()
    {
        var sender = new StubSender().Responds<GetFlashCardsQuery>(Deck(1));

        await new FlashCardsTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "count": 5, "reviewStage": 2, "dueOnly": true }"""));

        var query = sender.OnlySent<GetFlashCardsQuery>();
        Assert.Equal(5, query.Count);
        Assert.Equal((int)ReviewStage.Reinforced, query.ReviewStage);
        Assert.True(query.UseDaily);
    }

    [Fact]
    public async Task Reads_a_boolean_the_model_sent_as_a_string()
    {
        var sender = new StubSender().Responds<GetFlashCardsQuery>(Deck(1));

        await new FlashCardsTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "dueOnly": "true" }"""));

        Assert.True(sender.OnlySent<GetFlashCardsQuery>().UseDaily);
    }

    [Theory]
    [InlineData("""{ "count": 500 }""", 25)]
    [InlineData("""{ "count": 0 }""", 1)]
    public async Task Keeps_the_deck_within_one_turn(string args, int expected)
    {
        var options = new AgentLoopOptions { MaxToolResultItems = 25 };
        var sender = new StubSender().Responds<GetFlashCardsQuery>(Deck(1));

        await new FlashCardsTool(sender, options).ExecuteAsync(Json.Args(args));

        Assert.Equal(expected, sender.OnlySent<GetFlashCardsQuery>().Count);
    }

    [Fact]
    public async Task Says_so_in_words_when_no_card_matches()
    {
        var sender = new StubSender().Responds<GetFlashCardsQuery>(new List<VocabDtos.FlashCardDto>());

        var output = await new FlashCardsTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs());

        Assert.Equal("No saved words match those filters, so there is no deck to draw.", output);
    }

    [Fact]
    public async Task Returns_the_deck_as_cards()
    {
        var sender = new StubSender().Responds<GetFlashCardsQuery>(Deck(2));

        var output = await new FlashCardsTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs());

        using var json = Json.Parse(output);
        Assert.Equal(2, json.RootElement.GetProperty("cards").GetArrayLength());
        Assert.Equal("word-1", json.RootElement.GetProperty("cards")[0].GetProperty("word").GetString());
    }
}

public class GenerateExamToolTests
{
    private static List<VocabDtos.VocabExamQuestionDto> Exam(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new VocabDtos.VocabExamQuestionDto($"q{i}", $"v{i}", 2, $"word-{i}", null, null))
            .ToList();

    [Fact]
    public async Task Builds_five_questions_by_default()
    {
        var sender = new StubSender().Responds<GenerateVocabExamQuery>(Exam(5));

        await new GenerateExamTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs());

        var query = sender.OnlySent<GenerateVocabExamQuery>();
        Assert.Equal(5, query.QuestionCount);
        Assert.Null(query.From);
        Assert.Null(query.To);
    }

    [Theory]
    [InlineData("""{ "questionCount": 500 }""", 25)]
    [InlineData("""{ "questionCount": 0 }""", 1)]
    [InlineData("""{ "questionCount": 8 }""", 8)]
    public async Task Keeps_the_exam_to_a_length_it_can_actually_ask(string args, int expected)
    {
        var options = new AgentLoopOptions { MaxToolResultItems = 25 };
        var sender = new StubSender().Responds<GenerateVocabExamQuery>(Exam(1));

        await new GenerateExamTool(sender, options).ExecuteAsync(Json.Args(args));

        Assert.Equal(expected, sender.OnlySent<GenerateVocabExamQuery>().QuestionCount);
    }

    [Fact]
    public async Task Narrows_the_exam_to_a_date_range()
    {
        var sender = new StubSender().Responds<GenerateVocabExamQuery>(Exam(1));

        await new GenerateExamTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "notedFrom": "2026-01-01", "notedTo": "2026-01-31" }"""));

        var query = sender.OnlySent<GenerateVocabExamQuery>();
        Assert.Equal(new DateTime(2026, 1, 1), query.From!.Value.DateTime);
        Assert.Equal(new DateTime(2026, 1, 31), query.To!.Value.DateTime);
    }

    [Fact]
    public async Task Returns_the_questions()
    {
        var sender = new StubSender().Responds<GenerateVocabExamQuery>(Exam(3));

        var output = await new GenerateExamTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs());

        using var json = Json.Parse(output);
        Assert.Equal(3, json.RootElement.GetProperty("questions").GetArrayLength());
    }
}

public class SaveVocabToolTests
{
    private static PagedResult<VocabDtos.VocabResponseDto> Page(params VocabDtos.VocabResponseDto[] items)
        => new([.. items], 1, 25, items.Length);

    [Fact]
    public async Task Searches_before_it_writes_and_then_reads_the_new_word_back()
    {
        var lookups = 0;
        var sender = new StubSender()
            .Responds<GetVocabsQuery>(_ => ++lookups == 1
                ? Page()
                : Page(Dto.Vocab("serendipity", id: "v1")))
            .Responds<CreateVocabCommand>(Unit.Value);

        var output = await new SaveVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args("""
            { "word": "serendipity", "content": "(n) a happy accident" }
            """));

        var created = sender.OnlySent<CreateVocabCommand>();
        Assert.Equal("serendipity", created.Word);
        Assert.Equal("(n) a happy accident", created.Content);

        // Searched, created, then searched again to recover the id.
        Assert.Equal(2, sender.SentOf<GetVocabsQuery>().Count);

        using var json = Json.Parse(output);
        Assert.Equal("v1", json.RootElement.GetProperty("saved").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Refuses_a_word_that_is_already_saved()
    {
        var sender = new StubSender()
            .Responds<GetVocabsQuery>(Page(Dto.Vocab("serendipity", id: "v1", reviewStage: 2)));

        var output = await new SaveVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args("""
            { "word": "serendipity", "content": "(n) a happy accident" }
            """));

        Assert.False(sender.SentAny<CreateVocabCommand>());
        Assert.Contains("already saved", output);
        Assert.Contains("v1", output);
        Assert.Contains("review stage 2", output);
        Assert.Contains("update_vocab", output);
    }

    [Fact]
    public async Task The_duplicate_check_ignores_case()
    {
        var sender = new StubSender().Responds<GetVocabsQuery>(Page(Dto.Vocab("Serendipity")));

        var output = await new SaveVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args("""
            { "word": "serendipity", "content": "(n) a happy accident" }
            """));

        Assert.False(sender.SentAny<CreateVocabCommand>());
        Assert.Contains("already saved", output);
    }

    [Fact]
    public async Task A_near_match_from_the_substring_search_is_not_a_duplicate()
    {
        // The search matches on substring, so looking up "serene" can return "serendipity" — only an
        // exact word match may block the save.
        var lookups = 0;
        var sender = new StubSender()
            .Responds<GetVocabsQuery>(_ => ++lookups == 1
                ? Page(Dto.Vocab("serendipity"))
                : Page(Dto.Vocab("serene", id: "v2")))
            .Responds<CreateVocabCommand>(Unit.Value);

        await new SaveVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args("""
            { "word": "serene", "content": "(adj) calm" }
            """));

        Assert.Equal("serene", sender.OnlySent<CreateVocabCommand>().Word);
    }

    [Fact]
    public async Task Confirms_the_save_even_when_the_word_cannot_be_read_back()
    {
        var sender = new StubSender()
            .Responds<GetVocabsQuery>(Page())
            .Responds<CreateVocabCommand>(Unit.Value);

        var output = await new SaveVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args("""
            { "word": "serendipity", "content": "(n) a happy accident" }
            """));

        Assert.Equal("Saved 'serendipity'.", output);
    }

    [Theory]
    [InlineData("""{ "content": "a meaning" }""", "word")]
    [InlineData("""{ "word": "serendipity" }""", "content")]
    [InlineData("""{ "word": "   ", "content": "a meaning" }""", "word")]
    public async Task Refuses_a_half_written_entry(string args, string missing)
    {
        var sender = new StubSender();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => new SaveVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args(args)));

        Assert.Contains($"'{missing}' is required", error.Message);
        Assert.False(sender.SentAny<CreateVocabCommand>());
    }

    [Fact]
    public async Task Searches_no_wider_than_one_turn_allows()
    {
        var options = new AgentLoopOptions { MaxToolResultItems = 7 };
        var sender = new StubSender()
            .Responds<GetVocabsQuery>(Page())
            .Responds<CreateVocabCommand>(Unit.Value);

        await new SaveVocabTool(sender, options).ExecuteAsync(Json.Args("""
            { "word": "serendipity", "content": "(n) a happy accident" }
            """));

        Assert.All(sender.SentOf<GetVocabsQuery>(), query => Assert.Equal(7, query.PageSize));
    }
}

public class UpdateVocabToolTests
{
    [Fact]
    public async Task Sends_only_the_fields_the_model_supplied()
    {
        var sender = new StubSender()
            .Responds<UpdateVocabCommand>(Unit.Value)
            .Responds<GetVocabByIdQuery>(Dto.Vocab("serendipity", id: "v1"));

        await new UpdateVocabTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "id": "v1", "content": "a better meaning" }"""));

        var command = sender.OnlySent<UpdateVocabCommand>();
        Assert.Equal("v1", command.Id);
        Assert.Null(command.Word);
        Assert.Equal("a better meaning", command.Content);
    }

    [Fact]
    public async Task Reads_the_word_back_so_the_model_sees_what_it_wrote()
    {
        var sender = new StubSender()
            .Responds<UpdateVocabCommand>(Unit.Value)
            .Responds<GetVocabByIdQuery>(Dto.Vocab("receive", id: "v1"));

        var output = await new UpdateVocabTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "id": "v1", "word": "receive" }"""));

        Assert.Equal("v1", sender.OnlySent<GetVocabByIdQuery>().Id);

        using var json = Json.Parse(output);
        Assert.Equal("receive", json.RootElement.GetProperty("updated").GetProperty("word").GetString());
    }

    [Fact]
    public async Task Refuses_an_edit_that_changes_nothing()
    {
        var sender = new StubSender();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => new UpdateVocabTool(sender, new AgentLoopOptions())
                .ExecuteAsync(Json.Args("""{ "id": "v1" }""")));

        Assert.Contains("at least one of 'word' or 'content'", error.Message);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Treats_blank_fields_as_nothing_to_change()
    {
        var sender = new StubSender();

        await Assert.ThrowsAsync<ArgumentException>(
            () => new UpdateVocabTool(sender, new AgentLoopOptions())
                .ExecuteAsync(Json.Args("""{ "id": "v1", "word": "  ", "content": "" }""")));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Refuses_to_guess_at_a_missing_id()
    {
        var sender = new StubSender();

        await Assert.ThrowsAsync<ArgumentException>(
            () => new UpdateVocabTool(sender, new AgentLoopOptions())
                .ExecuteAsync(Json.Args("""{ "content": "a meaning" }""")));

        Assert.Empty(sender.Sent);
    }
}

public class ReviewVocabToolTests
{
    [Fact]
    public async Task Advances_the_word_then_reports_the_new_schedule()
    {
        var nextReview = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var sender = new StubSender()
            .Responds<ReviewVocabCommand>(Unit.Value)
            .Responds<GetVocabByIdQuery>(
                Dto.Vocab("serendipity", id: "v1", reviewStage: 1, nextReviewAt: nextReview));

        var output = await new ReviewVocabTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "id": "v1" }"""));

        Assert.Equal("v1", sender.OnlySent<ReviewVocabCommand>().Id);
        Assert.Equal("v1", sender.OnlySent<GetVocabByIdQuery>().Id);

        using var json = Json.Parse(output);
        Assert.Equal("serendipity", json.RootElement.GetProperty("word").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("reviewStage").GetInt32());
        Assert.False(json.RootElement.GetProperty("isCompleted").GetBoolean());
        Assert.True(json.RootElement.TryGetProperty("nextReviewAt", out _));
    }

    [Fact]
    public async Task Reports_a_word_that_has_just_been_mastered()
    {
        var sender = new StubSender()
            .Responds<ReviewVocabCommand>(Unit.Value)
            .Responds<GetVocabByIdQuery>(
                Dto.Vocab("serendipity", id: "v1", reviewStage: 3, nextReviewAt: null, isCompleted: true));

        var output = await new ReviewVocabTool(sender, new AgentLoopOptions())
            .ExecuteAsync(Json.Args("""{ "id": "v1" }"""));

        using var json = Json.Parse(output);
        Assert.Equal(3, json.RootElement.GetProperty("reviewStage").GetInt32());
        Assert.True(json.RootElement.GetProperty("isCompleted").GetBoolean());

        // Nulls are dropped from tool results, so an empty schedule shows up as an absent field.
        Assert.False(json.RootElement.TryGetProperty("nextReviewAt", out _));
    }

    [Fact]
    public async Task Refuses_to_guess_at_a_missing_id()
    {
        var sender = new StubSender();

        await Assert.ThrowsAsync<ArgumentException>(
            () => new ReviewVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.NoArgs()));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Lets_a_refusal_from_the_domain_surface_to_the_loop()
    {
        // The loop turns a throw into an error tool result the model can read and correct, so the
        // tool must not swallow it.
        var sender = new StubSender()
            .Responds<ReviewVocabCommand>(_ => throw new BadRequestException("already mastered"));

        var error = await Assert.ThrowsAsync<BadRequestException>(
            () => new ReviewVocabTool(sender, new AgentLoopOptions()).ExecuteAsync(Json.Args("""{ "id": "v1" }""")));

        Assert.Equal("already mastered", error.Message);
        Assert.False(sender.SentAny<GetVocabByIdQuery>());
    }
}

/// <summary>Response shapes the stubbed handlers hand back.</summary>
internal static class Dto
{
    public static VocabDtos.VocabResponseDto Vocab(
        string word,
        string id = "v1",
        int reviewStage = 0,
        DateTimeOffset? nextReviewAt = null,
        bool isCompleted = false)
        => new(
            id,
            word,
            "(n) a meaning",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            reviewStage,
            // A completed word has nothing scheduled, the same as the domain leaves it.
            isCompleted ? null : nextReviewAt ?? new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero),
            null,
            isCompleted,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "tester",
            null,
            null);
}

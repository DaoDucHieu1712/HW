using HW.Agentic.Abstractions;
using HW.Agentic.Core;
using HW.Application.Features.Vocabs.Agent;
using HW.Application.Features.Vocabs.Dtos;
using HW.Application.Features.Vocabs.Queries.AskVocabAgent;

namespace HW.UnitTests.Application.Agent;

public class AskVocabAgentQueryHandlerTests
{
    private sealed class RecordingAgent : IVocabAgent
    {
        public VocabAgentDtos.AskVocabAgentRequestDto? Received { get; private set; }

        public CancellationToken Token { get; private set; }

        public Task<VocabAgentDtos.VocabAgentResponseDto> RunAsync(
            VocabAgentDtos.AskVocabAgentRequestDto request, CancellationToken ct = default)
        {
            Received = request;
            Token = ct;

            return Task.FromResult(new VocabAgentDtos.VocabAgentResponseDto(
                "answered", 2, false, "end_turn", [], new VocabAgentDtos.VocabAgentUsageDto(1, 2, 3, 4)));
        }
    }

    [Fact]
    public async Task Hands_the_question_to_the_agent_and_returns_its_answer()
    {
        var agent = new RecordingAgent();
        var request = new VocabAgentDtos.AskVocabAgentRequestDto("what should I study today?");

        var response = await new AskVocabAgentQueryHandler(agent)
            .Handle(new AskVocabAgentQuery(request), default);

        Assert.Same(request, agent.Received);
        Assert.Equal("answered", response.Answer);
        Assert.Equal(2, response.Iterations);
        Assert.Equal("end_turn", response.StopReason);
    }

    [Fact]
    public async Task Passes_the_cancellation_token_along()
    {
        var agent = new RecordingAgent();
        using var cts = new CancellationTokenSource();

        await new AskVocabAgentQueryHandler(agent).Handle(
            new AskVocabAgentQuery(new VocabAgentDtos.AskVocabAgentRequestDto("a question")), cts.Token);

        Assert.Equal(cts.Token, agent.Token);
    }
}

public class AskVocabAgentQueryValidatorTests
{
    private readonly AskVocabAgentQueryValidator _validator = new();

    private static AskVocabAgentQuery Ask(string question)
        => new(new VocabAgentDtos.AskVocabAgentRequestDto(question));

    [Fact]
    public void Accepts_a_question()
    {
        Assert.True(_validator.Validate(Ask("what should I study today?")).IsValid);
    }

    [Fact]
    public void Rejects_a_body_that_did_not_deserialize()
    {
        // Cascade(Stop) is what keeps the question rule from walking into the null.
        var result = _validator.Validate(new AskVocabAgentQuery(null!));

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_question(string question)
    {
        Assert.False(_validator.Validate(Ask(question)).IsValid);
    }

    [Fact]
    public void Rejects_a_question_longer_than_four_thousand_characters()
    {
        Assert.False(_validator.Validate(Ask(new string('a', 4_001))).IsValid);
    }

    [Fact]
    public void Accepts_a_question_of_exactly_four_thousand_characters()
    {
        Assert.True(_validator.Validate(Ask(new string('a', 4_000))).IsValid);
    }
}

public class VocabAgentLoopTests
{
    private sealed class StubEngineerLoop : IEngineerLoop
    {
        private readonly AgentRunResult _result;

        public StubEngineerLoop(AgentRunResult result) => _result = result;

        public AgentRunRequest? Received { get; private set; }

        public Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken ct = default)
        {
            Received = request;
            return Task.FromResult(_result);
        }
    }

    private static AgentRunResult Result(
        string answer = "here is your mission",
        bool budgetExhausted = false,
        IReadOnlyList<AgentStep>? steps = null)
        => new(
            "vocab-coach",
            LlmProvider.Claude,
            answer,
            3,
            budgetExhausted,
            "end_turn",
            steps ?? [],
            new AgentUsage(100, 50, 10, 5),
            1_234);

    [Fact]
    public async Task Runs_the_question_as_the_vocab_coach()
    {
        var loop = new StubEngineerLoop(Result());

        await new VocabAgentLoop(loop).RunAsync(
            new VocabAgentDtos.AskVocabAgentRequestDto("what should I study today?"));

        Assert.NotNull(loop.Received);
        Assert.Equal("what should I study today?", loop.Received!.Task);
        Assert.Equal("vocab-coach", loop.Received.Agent.Name);
        Assert.Equal(VocabAgentLoop.ToolNames, loop.Received.Agent.Tools);
    }

    [Fact]
    public async Task Carries_the_mutation_and_web_lookup_switches_into_the_run()
    {
        var loop = new StubEngineerLoop(Result());

        await new VocabAgentLoop(loop).RunAsync(new VocabAgentDtos.AskVocabAgentRequestDto(
            "look this word up", AllowMutations: false, EnableWebLookup: true));

        Assert.False(loop.Received!.AllowMutations);
        Assert.True(loop.Received.Agent.EnableWebLookup);
    }

    [Fact]
    public async Task Replays_the_earlier_turns_of_the_conversation()
    {
        var loop = new StubEngineerLoop(Result());

        await new VocabAgentLoop(loop).RunAsync(new VocabAgentDtos.AskVocabAgentRequestDto(
            "and the next one?",
            History:
            [
                new VocabAgentDtos.VocabAgentTurnDto("user", "quiz me"),
                new VocabAgentDtos.VocabAgentTurnDto("assistant", "what does 'serendipity' mean?"),
            ]));

        var history = loop.Received!.History;
        Assert.NotNull(history);
        Assert.Equal(2, history!.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("quiz me", history[0].Text);
        Assert.Equal("assistant", history[1].Role);
    }

    [Fact]
    public async Task A_first_turn_carries_no_history()
    {
        var loop = new StubEngineerLoop(Result());

        await new VocabAgentLoop(loop).RunAsync(new VocabAgentDtos.AskVocabAgentRequestDto("a question"));

        Assert.Null(loop.Received!.History);
    }

    [Fact]
    public async Task Maps_the_run_result_onto_the_vocab_contract()
    {
        var loop = new StubEngineerLoop(Result(
            answer: "three words are due",
            steps: [new AgentStep(1, "daily_mission", "{}", "3 words", false, 42)]));

        var response = await new VocabAgentLoop(loop).RunAsync(
            new VocabAgentDtos.AskVocabAgentRequestDto("what should I study today?"));

        Assert.Equal("three words are due", response.Answer);
        Assert.Equal(3, response.Iterations);
        Assert.False(response.BudgetExhausted);
        Assert.Equal("end_turn", response.StopReason);

        var step = Assert.Single(response.Steps);
        Assert.Equal(1, step.Iteration);
        Assert.Equal("daily_mission", step.Tool);
        Assert.Equal("{}", step.Input);
        Assert.Equal("3 words", step.Output);
        Assert.False(step.IsError);
        Assert.Equal(42, step.ElapsedMs);

        Assert.Equal(100, response.Usage.InputTokens);
        Assert.Equal(50, response.Usage.OutputTokens);
        Assert.Equal(10, response.Usage.CacheReadInputTokens);
        Assert.Equal(5, response.Usage.CacheCreationInputTokens);
    }

    [Fact]
    public async Task Reports_an_answer_the_loop_had_to_cut_short()
    {
        var loop = new StubEngineerLoop(Result(budgetExhausted: true));

        var response = await new VocabAgentLoop(loop).RunAsync(
            new VocabAgentDtos.AskVocabAgentRequestDto("a question"));

        Assert.True(response.BudgetExhausted);
    }

    [Fact]
    public async Task Reports_a_tool_that_failed()
    {
        var loop = new StubEngineerLoop(Result(
            steps: [new AgentStep(2, "get_vocab", """{"id":"nope"}""", "not found", true, 7)]));

        var response = await new VocabAgentLoop(loop).RunAsync(
            new VocabAgentDtos.AskVocabAgentRequestDto("a question"));

        Assert.True(Assert.Single(response.Steps).IsError);
    }

    [Fact]
    public void The_coach_is_never_handed_the_developer_tools()
    {
        // The tool list is named rather than inherited so that adding a workspace or SQL tool cannot
        // silently widen what the vocab agent can reach.
        Assert.All(
            VocabAgentLoop.ToolNames,
            name => Assert.DoesNotContain(name, new[] { "read_file", "propose_patch", "trace_sql", "trace_log" }));
    }
}

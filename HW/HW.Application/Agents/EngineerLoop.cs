using System.Diagnostics;
using System.Text.Json;
using HW.Application.Abstractions.AI;
using Microsoft.Extensions.Logging;

namespace HW.Application.Agents;

public interface IEngineerLoop
{
    Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken ct = default);
}

/// <summary>
/// The agentic loop every agent runs on: ask the model, execute whatever tools it asked for, hand
/// the results back, repeat until it answers or the budget runs out.
///
/// <para>
/// The loop owns the conversation and re-sends it whole each round-trip; the provider adapters are
/// stateless. Assistant turns are replayed through their opaque provider blocks
/// (<see cref="AgentTurn.ProviderContent"/>) rather than rebuilt from text — Claude's thinking
/// blocks carry a signature the API rejects if it is altered, and OpenAI's tool_calls have to come
/// back verbatim for the tool results to correlate. That replay is provider-shaped, which is why
/// <b>a single run stays on one provider</b>: switching mid-conversation would feed one vendor's
/// blocks to another. A fan-out therefore gives each provider its own fresh conversation.
/// </para>
///
/// <para>
/// Every way out is bounded. The iteration cap stops a model that keeps reaching for tools; a
/// failing tool comes back as an error result instead of an exception, so the model can recover
/// rather than the run dying; and an exhausted budget still produces an answer, from a final call
/// made with tool use withheld.
/// </para>
/// </summary>
public sealed class EngineerLoop : IEngineerLoop
{
    private const string WrapUpInstruction =
        "You have run out of tool calls for this task. Answer now from what you already found, " +
        "say plainly what you could not finish, and do not ask for another tool.";

    private readonly IAgentChatClientResolver _resolver;
    private readonly IReadOnlyList<IAgentTool> _tools;
    private readonly AgentLoopOptions _options;
    private readonly ILogger<EngineerLoop> _logger;

    public EngineerLoop(
        IAgentChatClientResolver resolver,
        IEnumerable<IAgentTool> tools,
        AgentLoopOptions options,
        ILogger<EngineerLoop> logger)
    {
        _resolver = resolver;
        _tools = tools.ToList();
        _options = options;
        _logger = logger;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken ct = default)
    {
        var agent = request.Agent;
        var provider = request.ProviderOverride ?? agent.Provider;
        var chat = _resolver.Resolve(provider);
        var maxIterations = Math.Max(agent.MaxIterations ?? _options.MaxIterations, 1);
        var started = Stopwatch.GetTimestamp();

        var available = SelectTools(agent, request.AllowMutations);

        var definitions = available.Values
            .Select(tool => new AgentToolDefinition(tool.Name, tool.Description, tool.InputSchemaJson))
            .ToList();

        var messages = BuildInitialMessages(request);
        var steps = new List<AgentStep>();
        var usage = AgentUsage.Zero;
        var iteration = 0;

        while (true)
        {
            iteration++;

            var turn = await chat.CompleteAsync(
                new AgentCompletionRequest(
                    agent.SystemPrompt,
                    messages,
                    definitions,
                    agent.EnableWebLookup,
                    AllowToolUse: true,
                    agent.Model),
                ct);

            usage += turn.Usage;
            messages.Add(AgentMessage.Assistant(turn));

            // A server-side tool paused the turn. Nothing to run here — re-sending the conversation
            // lets the provider pick up where it left off.
            if (turn.StopReason == AgentStopReason.PauseTurn)
            {
                if (iteration >= maxIterations) break;
                continue;
            }

            if (turn.StopReason != AgentStopReason.ToolUse)
            {
                return Respond(
                    agent, provider, turn.Text, turn.StopReason, iteration,
                    budgetExhausted: false, steps, usage, started);
            }

            var results = new List<AgentToolResult>(turn.ToolCalls.Count);

            // Sequential, not parallel: tools reach scoped services — a shared EF Core DbContext
            // among them — which cannot serve two concurrent operations. Parallelism in this system
            // lives one level up, where the workflow gives each branch its own scope.
            foreach (var call in turn.ToolCalls)
            {
                var (result, step) = await ExecuteAsync(available, call, iteration, ct);
                results.Add(result);
                steps.Add(step);
            }

            // One user message carrying every result. Splitting them across several messages teaches
            // the model to stop asking for parallel calls.
            messages.Add(AgentMessage.FromToolResults(results));

            if (iteration >= maxIterations) break;
        }

        _logger.LogWarning(
            "Agent {Agent} on {Provider} hit its {MaxIterations}-iteration budget after {ToolCalls} tool calls; forcing a wrap-up.",
            agent.Name, provider, maxIterations, steps.Count);

        // Out of budget but mid-task. Rather than returning a half-finished turn, ask once more with
        // tool use withheld so the model has to answer from what it already gathered.
        var wrapUp = await chat.CompleteAsync(
            new AgentCompletionRequest(
                agent.SystemPrompt,
                [.. messages, AgentMessage.User(WrapUpInstruction)],
                definitions,
                agent.EnableWebLookup,
                AllowToolUse: false,
                agent.Model),
            ct);

        usage += wrapUp.Usage;

        return Respond(
            agent, provider, wrapUp.Text, wrapUp.StopReason, iteration,
            budgetExhausted: true, steps, usage, started);
    }

    /// <summary>
    /// Narrows the registered tools to what this agent may use. An unknown name in a definition is
    /// a wiring mistake worth surfacing at run time rather than silently handing the agent fewer
    /// tools than its author intended.
    /// </summary>
    private Dictionary<string, IAgentTool> SelectTools(AgentDefinition agent, bool allowMutations)
    {
        var candidates = _tools.Where(tool => allowMutations || !tool.IsMutating);

        // An empty list means no tools, not every tool. The other reading is a footgun: an agent
        // written before a feature existed would silently inherit that feature's tools later.
        if (agent.Tools.Count == 0)
            return new Dictionary<string, IAgentTool>(StringComparer.Ordinal);

        var wanted = new HashSet<string>(agent.Tools, StringComparer.Ordinal);
        var selected = candidates.Where(tool => wanted.Contains(tool.Name))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        // Only complain about names no registered tool has at all — a write tool dropped by
        // read-only mode is expected, not a misconfiguration.
        var registered = _tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = wanted.Where(name => !registered.Contains(name)).ToList();

        if (unknown.Count > 0)
        {
            _logger.LogWarning(
                "Agent {Agent} lists tools that are not registered: {Unknown}.",
                agent.Name, string.Join(", ", unknown));
        }

        return selected;
    }

    private static List<AgentMessage> BuildInitialMessages(AgentRunRequest request)
    {
        var messages = new List<AgentMessage>();

        foreach (var past in request.History ?? [])
        {
            if (string.IsNullOrWhiteSpace(past.Text)) continue;

            messages.Add(string.Equals(past.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? AgentMessage.AssistantText(past.Text)
                : AgentMessage.User(past.Text));
        }

        messages.Add(AgentMessage.User(request.Task));
        return messages;
    }

    private async Task<(AgentToolResult Result, AgentStep Step)> ExecuteAsync(
        IReadOnlyDictionary<string, IAgentTool> available,
        AgentToolCall call,
        int iteration,
        CancellationToken ct)
    {
        var arguments = call.Input.ValueKind == JsonValueKind.Undefined ? "{}" : call.Input.GetRawText();
        var started = Stopwatch.GetTimestamp();

        string output;
        bool isError;

        if (!available.TryGetValue(call.Name, out var tool))
        {
            // Either the model invented a tool, or it asked for one this agent may not use. Naming
            // the tools it does have turns a dead end into a recoverable turn.
            output = $"No tool named '{call.Name}' is available to you. Available tools: {string.Join(", ", available.Keys)}.";
            isError = true;
        }
        else
        {
            try
            {
                output = await tool.ExecuteAsync(call.Input, ct);
                isError = false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller walked away — abandon the run rather than reporting the cancellation to
                // the model as a tool failure it might retry.
                throw;
            }
            catch (Exception ex)
            {
                // Reported, not thrown: a bad argument or a missing row is something the model can
                // correct next turn, and killing the run would throw away the work already done.
                _logger.LogWarning(ex, "Tool {Tool} failed with arguments {Arguments}.", call.Name, arguments);
                output = $"{ex.GetType().Name}: {ex.Message}";
                isError = true;
            }
        }

        output = Truncate(output, _options.MaxToolResultChars);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        return (
            new AgentToolResult(call.Id, output, isError),
            new AgentStep(iteration, call.Name, arguments, output, isError, elapsedMs));
    }

    private static string Truncate(string value, int max)
        => value.Length <= max
            ? value
            : value[..max] + $"\n\n[truncated at {max} characters — narrow the query to see the rest]";

    /// <summary>
    /// Two stop reasons leave the caller holding something that does not read as an answer: a
    /// refusal returns no content at all, and a capped turn returns a sentence that stops mid-word.
    /// Saying which one happened beats handing back a blank or a fragment.
    /// </summary>
    private static string Explain(string answer, AgentStopReason stopReason) => stopReason switch
    {
        AgentStopReason.Refusal =>
            "The model declined to answer this one. Try rewording the task.",

        AgentStopReason.MaxTokens =>
            answer + "\n\n[cut off at the response limit — ask for the rest, or for a shorter answer]",

        _ when string.IsNullOrWhiteSpace(answer) =>
            "The model returned no answer. Try running it again.",

        _ => answer,
    };

    private static AgentRunResult Respond(
        AgentDefinition agent,
        LlmProvider provider,
        string answer,
        AgentStopReason stopReason,
        int iterations,
        bool budgetExhausted,
        List<AgentStep> steps,
        AgentUsage usage,
        long started)
        => new(
            agent.Name,
            provider,
            Explain(answer, stopReason),
            iterations,
            budgetExhausted,
            stopReason.ToString(),
            steps,
            usage,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
}

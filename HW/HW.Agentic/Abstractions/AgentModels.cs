using System.Text.Json;

namespace HW.Agentic.Abstractions;

/// <summary>Who authored a turn in the conversation the loop keeps.</summary>
public enum AgentRole
{
    User = 0,
    Assistant = 1
}

/// <summary>
/// Why the model stopped generating. The loop branches on this, so it is deliberately
/// provider-neutral — an adapter maps its own wire values onto these.
/// </summary>
public enum AgentStopReason
{
    /// <summary>The model finished its answer. The loop is done.</summary>
    EndTurn = 0,

    /// <summary>The model asked for tools. The loop must execute them and send the results back.</summary>
    ToolUse = 1,

    /// <summary>The output cap was hit mid-thought. The answer is truncated.</summary>
    MaxTokens = 2,

    /// <summary>
    /// A long-running server-side tool (web lookup) paused the turn. Nothing to execute locally —
    /// the loop just re-sends the conversation to let the provider carry on.
    /// </summary>
    PauseTurn = 3,

    /// <summary>The provider's safety classifier declined. There is no content to use.</summary>
    Refusal = 4,

    Other = 5
}

/// <summary>One tool the model asked to call inside a single assistant turn.</summary>
/// <param name="Id">Correlates the call with its <see cref="AgentToolResult"/>. Every call needs one.</param>
public sealed record AgentToolCall(string Id, string Name, JsonElement Input);

/// <summary>What the loop feeds back for one <see cref="AgentToolCall"/>.</summary>
/// <param name="IsError">
/// Reports the failure to the model rather than throwing. The model can then retry with different
/// arguments or explain the problem — a thrown exception would just kill the run.
/// </param>
public sealed record AgentToolResult(string ToolCallId, string Content, bool IsError = false);

public sealed record AgentUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadInputTokens,
    long CacheCreationInputTokens)
{
    public static readonly AgentUsage Zero = new(0, 0, 0, 0);

    public static AgentUsage operator +(AgentUsage left, AgentUsage right) => new(
        left.InputTokens + right.InputTokens,
        left.OutputTokens + right.OutputTokens,
        left.CacheReadInputTokens + right.CacheReadInputTokens,
        left.CacheCreationInputTokens + right.CacheCreationInputTokens);
}

/// <summary>One assistant turn as the loop sees it.</summary>
/// <param name="ProviderContent">
/// The provider's own content blocks for this turn, kept opaque. The loop never inspects it; it only
/// hands it back on the next request so the assistant turn is replayed byte-for-byte. That matters
/// because thinking blocks carry a signature the API rejects if tampered with, and server-tool
/// blocks have no neutral representation worth inventing.
/// </param>
public sealed record AgentTurn(
    string Text,
    string? Thinking,
    IReadOnlyList<AgentToolCall> ToolCalls,
    AgentStopReason StopReason,
    AgentUsage Usage,
    object? ProviderContent);

/// <summary>
/// A message in the running conversation. Exactly one of the three payloads is set:
/// <see cref="Text"/> for a plain user turn, <see cref="ToolResults"/> for the user turn that
/// answers tool calls, or <see cref="ProviderContent"/> for a replayed assistant turn.
/// </summary>
public sealed class AgentMessage
{
    private AgentMessage(AgentRole role) => Role = role;

    public AgentRole Role { get; }

    public string? Text { get; private init; }

    public IReadOnlyList<AgentToolResult>? ToolResults { get; private init; }

    public object? ProviderContent { get; private init; }

    public static AgentMessage User(string text) => new(AgentRole.User) { Text = text };

    /// <summary>
    /// All results for one assistant turn go in a single user message. Splitting them across
    /// several messages trains the model out of asking for parallel calls.
    /// </summary>
    public static AgentMessage FromToolResults(IReadOnlyList<AgentToolResult> results)
        => new(AgentRole.User) { ToolResults = results };

    public static AgentMessage Assistant(AgentTurn turn)
        => new(AgentRole.Assistant) { Text = turn.Text, ProviderContent = turn.ProviderContent };

    /// <summary>
    /// An assistant turn replayed from outside this process — a prior turn posted back by the
    /// client. It carries text only: the provider blocks are gone, so tool calls made back then
    /// cannot be replayed and the model sees just what it said.
    /// </summary>
    public static AgentMessage AssistantText(string text)
        => new(AgentRole.Assistant) { Text = text };
}

/// <summary>A tool as advertised to the model. <paramref name="InputSchemaJson"/> is a JSON Schema object.</summary>
public sealed record AgentToolDefinition(string Name, string Description, string InputSchemaJson);

/// <param name="AllowToolUse">
/// False on the wrap-up call the loop makes after exhausting its iteration budget: the model has to
/// answer from what it already gathered instead of asking for one more tool.
/// </param>
/// <param name="Model">
/// Overrides the adapter's configured model for this call — how one agent runs on a cheaper model
/// than another without a second registration. Null keeps the adapter's default.
/// </param>
/// <param name="MaxTokens">Overrides the adapter's configured output cap. Null keeps the default.</param>
public sealed record AgentCompletionRequest(
    string SystemPrompt,
    IReadOnlyList<AgentMessage> Messages,
    IReadOnlyList<AgentToolDefinition> Tools,
    bool EnableWebLookup = false,
    bool AllowToolUse = true,
    string? Model = null,
    int? MaxTokens = null);

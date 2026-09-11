using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using HW.Agentic.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HW.Agentic.Providers;

/// <summary>
/// Claude behind <see cref="IAgentChatClient"/>. Stateless by design — the loop owns the
/// conversation and re-sends it every round-trip, so this type only translates between the
/// provider-neutral model and the Anthropic SDK.
/// </summary>
public sealed class AnthropicAgentChatClient : IAgentChatClient
{
    private readonly AnthropicClient _client;
    private readonly AnthropicAgentOptions _options;
    private readonly ILogger<AnthropicAgentChatClient> _logger;

    public AnthropicAgentChatClient(
        AnthropicClient client,
        IOptions<AnthropicAgentOptions> options,
        ILogger<AnthropicAgentChatClient> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public LlmProvider Provider => LlmProvider.Claude;

    public async Task<AgentTurn> CompleteAsync(AgentCompletionRequest request, CancellationToken ct = default)
    {
        var parameters = new MessageCreateParams
        {
            Model = request.Model ?? _options.Model,
            MaxTokens = request.MaxTokens ?? _options.MaxTokens,

            // The system prompt is a cache breakpoint. It is constant across every request the app
            // makes, so from the second call on it is read from cache rather than re-charged.
            System = new List<TextBlockParam>
            {
                new()
                {
                    Text = request.SystemPrompt,
                    CacheControl = new CacheControlEphemeral(),
                },
            },

            Thinking = new ThinkingConfigAdaptive
            {
                Display = _options.ShowThinking ? Display.Summarized : Display.Omitted,
            },

            OutputConfig = new OutputConfig { Effort = ParseEffort(_options.Effort) },
            Tools = BuildTools(request),
            Messages = BuildMessages(request.Messages),

            // On the loop's wrap-up call the tools stay declared — dropping them would leave the
            // earlier tool_use blocks in the history referring to tools the request no longer
            // defines — but the model is barred from asking for another one.
            ToolChoice = request.AllowToolUse ? null : new ToolChoiceNone(),
        };

        var response = await _client.Messages.Create(parameters, ct);

        return Translate(response);
    }

    private List<ToolUnion> BuildTools(AgentCompletionRequest request)
    {
        var tools = request.Tools
            .Select(definition => new ToolUnion(new Tool
            {
                Name = definition.Name,
                Description = definition.Description,
                InputSchema = ParseInputSchema(definition.InputSchemaJson),
            }))
            .ToList();

        // Server-side: Claude runs the search itself and the results come back inside the same
        // response, so there is nothing for the loop to execute.
        if (request.EnableWebLookup)
            tools.Add(new ToolUnion(new WebSearchTool20260209()));

        return tools;
    }

    /// <summary>
    /// Splits a tool's JSON Schema into the shape the SDK wants. <c>type: "object"</c> is set by the
    /// constructor, so only the properties and the required list are carried over.
    /// </summary>
    private static InputSchema ParseInputSchema(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        var root = document.RootElement;

        var properties = new Dictionary<string, JsonElement>();

        if (root.TryGetProperty("properties", out var propertyBag))
        {
            foreach (var property in propertyBag.EnumerateObject())
                properties[property.Name] = property.Value.Clone();
        }

        var required = root.TryGetProperty("required", out var requiredList)
            ? requiredList.EnumerateArray().Select(item => item.GetString()!).ToList()
            : [];

        return new InputSchema
        {
            Properties = properties,
            Required = required,
        };
    }

    private static List<MessageParam> BuildMessages(IReadOnlyList<AgentMessage> messages)
    {
        var built = new List<MessageParam>(messages.Count);

        foreach (var message in messages)
        {
            if (message.Role == AgentRole.Assistant)
            {
                // Replayed verbatim when the loop kept the provider's own blocks — that is what
                // keeps a thinking block's signature intact. A turn restored from client-side
                // history has only text.
                if (message.ProviderContent is List<ContentBlockParam> blocks && blocks.Count > 0)
                {
                    built.Add(new MessageParam { Role = Role.Assistant, Content = blocks });
                }
                else if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    built.Add(new MessageParam { Role = Role.Assistant, Content = message.Text });
                }

                continue;
            }

            if (message.ToolResults is { Count: > 0 } results)
            {
                var resultBlocks = results
                    .Select(result => (ContentBlockParam)new ToolResultBlockParam
                    {
                        ToolUseID = result.ToolCallId,
                        Content = result.Content,
                        IsError = result.IsError,
                    })
                    .ToList();

                built.Add(new MessageParam { Role = Role.User, Content = resultBlocks });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(message.Text))
                built.Add(new MessageParam { Role = Role.User, Content = message.Text });
        }

        return built;
    }

    /// <summary>
    /// Turns one response into the loop's view of it: the text to show, the calls to run, and the
    /// blocks to replay next turn.
    /// </summary>
    private AgentTurn Translate(Message response)
    {
        var text = new List<string>();
        var thinking = new List<string>();
        var calls = new List<AgentToolCall>();
        var replay = new List<ContentBlockParam>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                text.Add(textBlock.Text);
                replay.Add(new TextBlockParam { Text = textBlock.Text });
            }
            else if (block.TryPickThinking(out var thinkingBlock))
            {
                if (!string.IsNullOrEmpty(thinkingBlock.Thinking))
                    thinking.Add(thinkingBlock.Thinking);

                // The signature has to survive untouched — the API rejects a thinking block whose
                // content and signature no longer agree.
                replay.Add(new ThinkingBlockParam
                {
                    Thinking = thinkingBlock.Thinking,
                    Signature = thinkingBlock.Signature,
                });
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                replay.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                calls.Add(new AgentToolCall(toolUse.ID, toolUse.Name, ToJsonElement(toolUse.Input)));

                replay.Add(new ToolUseBlockParam
                {
                    ID = toolUse.ID,
                    Name = toolUse.Name,
                    Input = toolUse.Input,
                });
            }
            else if (block.TryPickServerToolUse(out var serverToolUse))
            {
                // Web search ran on Anthropic's side. Nothing here is ours to interpret, so it is
                // round-tripped as raw JSON rather than rebuilt field by field.
                replay.Add(ServerToolUseBlockParam.FromRawUnchecked(serverToolUse.RawData));
            }
            else if (block.TryPickWebSearchToolResult(out var webSearchResult))
            {
                replay.Add(WebSearchToolResultBlockParam.FromRawUnchecked(webSearchResult.RawData));
            }
            else
            {
                // A block type this adapter has no mapping for. Dropping it silently would corrupt
                // the replayed turn, so say so once and carry on with what is understood.
                _logger.LogWarning("Dropping an unmapped Claude content block from the replayed assistant turn.");
            }
        }

        return new AgentTurn(
            string.Join("\n\n", text),
            thinking.Count == 0 ? null : string.Join("\n\n", thinking),
            calls,
            MapStopReason(response.StopReason?.ToString(), calls.Count > 0),
            new AgentUsage(
                response.Usage.InputTokens,
                response.Usage.OutputTokens,
                response.Usage.CacheReadInputTokens ?? 0,
                response.Usage.CacheCreationInputTokens ?? 0),
            replay);
    }

    private static JsonElement ToJsonElement(IReadOnlyDictionary<string, JsonElement> input)
        => JsonSerializer.SerializeToElement(input);

    /// <summary>
    /// Normalises the wire value before matching, so this keeps working whether the SDK surfaces
    /// <c>tool_use</c> or <c>ToolUse</c>. A response carrying tool calls is treated as a tool turn
    /// regardless — that is a fact about the content, not about the label.
    /// </summary>
    private static AgentStopReason MapStopReason(string? stopReason, bool hasToolCalls)
    {
        if (hasToolCalls) return AgentStopReason.ToolUse;

        var normalized = (stopReason ?? string.Empty).Replace("_", string.Empty).ToLowerInvariant();

        return normalized switch
        {
            "endturn" or "stopsequence" => AgentStopReason.EndTurn,
            "tooluse" => AgentStopReason.ToolUse,
            "maxtokens" => AgentStopReason.MaxTokens,
            "pauseturn" => AgentStopReason.PauseTurn,
            "refusal" => AgentStopReason.Refusal,
            _ => AgentStopReason.Other,
        };
    }

    private static Effort ParseEffort(string? effort) => effort?.Trim().ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "max" => Effort.Max,
        _ => Effort.High,
    };
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HW.Agentic.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HW.Agentic.Providers;

/// <summary>
/// OpenAI behind <see cref="IAgentChatClient"/>, over the chat completions endpoint.
///
/// <para>
/// Written against the HTTP API rather than the OpenAI SDK on purpose: the loop needs four things
/// from a provider — messages, tools, tool results, and a stop reason — and hand-rolling those keeps
/// one more large dependency and one more type model out of the solution. The request and response
/// shapes are stable and small enough that the trade pays.
/// </para>
/// </summary>
public sealed class OpenAiAgentChatClient : IAgentChatClient
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    private readonly HttpClient _http;
    private readonly OpenAiAgentOptions _options;
    private readonly ILogger<OpenAiAgentChatClient> _logger;

    public OpenAiAgentChatClient(
        HttpClient http,
        IOptions<OpenAiAgentOptions> options,
        ILogger<OpenAiAgentChatClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri((_options.BaseUrl ?? DefaultBaseUrl).TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _http.DefaultRequestHeaders.Authorization = new("Bearer", _options.ApiKey);
    }

    public LlmProvider Provider => LlmProvider.OpenAI;

    public async Task<AgentTurn> CompleteAsync(AgentCompletionRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model ?? _options.Model,
            ["max_completion_tokens"] = request.MaxTokens ?? _options.MaxTokens,
            ["messages"] = BuildMessages(request),
        };

        if (request.Tools.Count > 0)
        {
            body["tools"] = BuildTools(request.Tools);

            // "none" still leaves the tools declared, which matters: the history holds tool_calls
            // that would be orphaned if the definitions disappeared.
            body["tool_choice"] = request.AllowToolUse ? "auto" : "none";
        }

        if (!string.IsNullOrWhiteSpace(_options.ReasoningEffort))
            body["reasoning_effort"] = _options.ReasoningEffort;

        using var response = await _http.PostAsJsonAsync("chat/completions", body, ct);
        var payload = await ReadOrThrow(response, ct);

        return Translate(payload);
    }

    private static JsonArray BuildMessages(AgentCompletionRequest request)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
        };

        foreach (var message in request.Messages)
        {
            if (message.Role == AgentRole.Assistant)
            {
                // Replay the vendor's own assistant object when the loop kept it — that is what
                // carries the tool_calls each tool result has to answer.
                if (message.ProviderContent is JsonNode assistant)
                {
                    messages.Add(JsonNode.Parse(assistant.ToJsonString()));
                }
                else if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = message.Text });
                }

                continue;
            }

            if (message.ToolResults is { Count: > 0 } results)
            {
                // Unlike Claude, OpenAI wants one message per result rather than one message
                // carrying all of them.
                foreach (var result in results)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = result.ToolCallId,
                        ["content"] = result.IsError ? "ERROR: " + result.Content : result.Content,
                    });
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(message.Text))
                messages.Add(new JsonObject { ["role"] = "user", ["content"] = message.Text });
        }

        return messages;
    }

    private static JsonArray BuildTools(IReadOnlyList<AgentToolDefinition> tools)
    {
        var array = new JsonArray();

        foreach (var tool in tools)
        {
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonSchemaSanitizer.AsIs(tool.InputSchemaJson),
                },
            });
        }

        return array;
    }

    private AgentTurn Translate(JsonNode payload)
    {
        var choice = payload["choices"]?.AsArray().FirstOrDefault();
        var message = choice?["message"];
        var text = message?["content"]?.GetValue<string>() ?? string.Empty;
        var finishReason = choice?["finish_reason"]?.GetValue<string>();

        var calls = new List<AgentToolCall>();

        foreach (var call in message?["tool_calls"]?.AsArray() ?? [])
        {
            var function = call?["function"];
            var name = function?["name"]?.GetValue<string>();
            var id = call?["id"]?.GetValue<string>();

            if (name is null || id is null) continue;

            // Arguments arrive as a JSON string, not an object — parsing rather than string-matching
            // is the only safe read, since escaping varies between models.
            var raw = function?["arguments"]?.GetValue<string>();
            calls.Add(new AgentToolCall(id, name, ParseArguments(raw, name)));
        }

        var usage = payload["usage"];

        return new AgentTurn(
            text,
            null,
            calls,
            MapStopReason(finishReason, calls.Count > 0),
            new AgentUsage(
                usage?["prompt_tokens"]?.GetValue<long>() ?? 0,
                usage?["completion_tokens"]?.GetValue<long>() ?? 0,
                usage?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? 0,
                0),
            message is null ? null : JsonNode.Parse(message.ToJsonString()));
    }

    private JsonElement ParseArguments(string? raw, string toolName)
    {
        if (string.IsNullOrWhiteSpace(raw)) return EmptyObject();

        try
        {
            return JsonDocument.Parse(raw).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            // Malformed arguments are the model's mistake, not a transport failure. Handing the loop
            // an empty object lets the tool report a missing-argument error the model can correct.
            _logger.LogWarning(ex, "OpenAI returned unparseable arguments for tool {Tool}.", toolName);
            return EmptyObject();
        }
    }

    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();

    private static AgentStopReason MapStopReason(string? finishReason, bool hasToolCalls)
    {
        if (hasToolCalls) return AgentStopReason.ToolUse;

        return finishReason switch
        {
            "stop" => AgentStopReason.EndTurn,
            "tool_calls" or "function_call" => AgentStopReason.ToolUse,
            "length" => AgentStopReason.MaxTokens,
            "content_filter" => AgentStopReason.Refusal,
            _ => AgentStopReason.Other,
        };
    }

    private static async Task<JsonNode> ReadOrThrow(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // The body carries the actual reason (bad model name, no quota, malformed tool schema);
            // the status alone sends whoever reads the log looking in the wrong place.
            throw new HttpRequestException(
                $"OpenAI returned {(int)response.StatusCode}: {Excerpt(raw)}",
                null,
                response.StatusCode);
        }

        return JsonNode.Parse(raw)
               ?? throw new HttpRequestException("OpenAI returned an empty body.");
    }

    private static string Excerpt(string body) => body.Length <= 500 ? body : body[..500] + "…";
}

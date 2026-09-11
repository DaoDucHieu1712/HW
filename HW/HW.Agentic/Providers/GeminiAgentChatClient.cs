using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HW.Agentic.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HW.Agentic.Providers;

/// <summary>
/// Gemini behind <see cref="IAgentChatClient"/>, over the generateContent endpoint.
///
/// <para>
/// Gemini's shape differs from the other two in three ways the adapter has to absorb: turns are
/// <c>contents</c> with the assistant role spelled <c>model</c>, the system prompt is a separate
/// <c>systemInstruction</c> rather than a message, and a function response is correlated by function
/// <i>name</i> — there is no call id. The loop needs ids, so one is synthesised; see
/// <see cref="ToolNameFromId"/>.
/// </para>
/// </summary>
public sealed class GeminiAgentChatClient : IAgentChatClient
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private readonly HttpClient _http;
    private readonly GeminiAgentOptions _options;
    private readonly ILogger<GeminiAgentChatClient> _logger;

    public GeminiAgentChatClient(
        HttpClient http,
        IOptions<GeminiAgentOptions> options,
        ILogger<GeminiAgentChatClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri((_options.BaseUrl ?? DefaultBaseUrl).TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        // Header auth rather than the key in the query string, so the key cannot end up in an
        // access log or a proxy trace.
        _http.DefaultRequestHeaders.Add("x-goog-api-key", _options.ApiKey);
    }

    public LlmProvider Provider => LlmProvider.Gemini;

    public async Task<AgentTurn> CompleteAsync(AgentCompletionRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } },
            },
            ["contents"] = BuildContents(request),
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = request.MaxTokens ?? _options.MaxTokens,
            },
        };

        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray
            {
                new JsonObject { ["functionDeclarations"] = BuildFunctionDeclarations(request.Tools) },
            };

            body["toolConfig"] = new JsonObject
            {
                ["functionCallingConfig"] = new JsonObject
                {
                    ["mode"] = request.AllowToolUse ? "AUTO" : "NONE",
                },
            };
        }

        var model = request.Model ?? _options.Model;

        using var response = await _http.PostAsJsonAsync($"models/{model}:generateContent", body, ct);
        var payload = await ReadOrThrow(response, ct);

        return Translate(payload);
    }

    private static JsonArray BuildContents(AgentCompletionRequest request)
    {
        var contents = new JsonArray();

        foreach (var message in request.Messages)
        {
            if (message.Role == AgentRole.Assistant)
            {
                if (message.ProviderContent is JsonNode content)
                {
                    contents.Add(JsonNode.Parse(content.ToJsonString()));
                }
                else if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    contents.Add(new JsonObject
                    {
                        ["role"] = "model",
                        ["parts"] = new JsonArray { new JsonObject { ["text"] = message.Text } },
                    });
                }

                continue;
            }

            if (message.ToolResults is { Count: > 0 } results)
            {
                var parts = new JsonArray();

                foreach (var result in results)
                {
                    parts.Add(new JsonObject
                    {
                        ["functionResponse"] = new JsonObject
                        {
                            ["name"] = ToolNameFromId(result.ToolCallId),

                            // The response must be an object, so a plain string result is wrapped.
                            // Errors are labelled rather than hidden — the model has to see that the
                            // call failed to be able to correct it.
                            ["response"] = new JsonObject
                            {
                                [result.IsError ? "error" : "result"] = result.Content,
                            },
                        },
                    });
                }

                contents.Add(new JsonObject { ["role"] = "user", ["parts"] = parts });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                contents.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = message.Text } },
                });
            }
        }

        return contents;
    }

    private static JsonArray BuildFunctionDeclarations(IReadOnlyList<AgentToolDefinition> tools)
    {
        var declarations = new JsonArray();

        foreach (var tool in tools)
        {
            declarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,

                // Gemini takes a subset of OpenAPI schema and rejects the whole request over an
                // unknown keyword, so the tool's schema is pruned rather than passed through.
                ["parameters"] = JsonSchemaSanitizer.ForGemini(tool.InputSchemaJson),
            });
        }

        return declarations;
    }

    private AgentTurn Translate(JsonNode payload)
    {
        var candidate = payload["candidates"]?.AsArray().FirstOrDefault();
        var content = candidate?["content"];
        var finishReason = candidate?["finishReason"]?.GetValue<string>();

        var text = new List<string>();
        var calls = new List<AgentToolCall>();
        var index = 0;

        foreach (var part in content?["parts"]?.AsArray() ?? [])
        {
            if (part?["text"] is JsonNode partText)
            {
                text.Add(partText.GetValue<string>());
                continue;
            }

            if (part?["functionCall"] is not JsonNode functionCall) continue;

            var name = functionCall["name"]?.GetValue<string>();
            if (name is null) continue;

            var arguments = functionCall["args"] is JsonNode args
                ? JsonSchemaSanitizer.ToElement(args)
                : EmptyObject();

            calls.Add(new AgentToolCall(MakeId(name, index++), name, arguments));
        }

        var usage = payload["usageMetadata"];

        return new AgentTurn(
            string.Join("\n\n", text),
            null,
            calls,
            MapStopReason(finishReason, calls.Count > 0),
            new AgentUsage(
                usage?["promptTokenCount"]?.GetValue<long>() ?? 0,
                usage?["candidatesTokenCount"]?.GetValue<long>() ?? 0,
                usage?["cachedContentTokenCount"]?.GetValue<long>() ?? 0,
                0),
            content is null ? null : JsonNode.Parse(content.ToJsonString()));
    }

    /// <summary>
    /// Gemini correlates a function response by name, but the loop is built on call ids, and one
    /// turn can hold two calls to the same function. The id therefore encodes the name plus an
    /// ordinal, and <see cref="ToolNameFromId"/> recovers the name when the result goes back.
    /// Tool names are snake_case, so the separator cannot collide with one.
    /// </summary>
    private static string MakeId(string toolName, int index) => $"{toolName}#{index}";

    private static string ToolNameFromId(string id)
    {
        var separator = id.LastIndexOf('#');
        return separator < 0 ? id : id[..separator];
    }

    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();

    private static AgentStopReason MapStopReason(string? finishReason, bool hasToolCalls)
    {
        if (hasToolCalls) return AgentStopReason.ToolUse;

        return finishReason switch
        {
            "STOP" => AgentStopReason.EndTurn,
            "MAX_TOKENS" => AgentStopReason.MaxTokens,
            "SAFETY" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" => AgentStopReason.Refusal,
            _ => AgentStopReason.Other,
        };
    }

    private async Task<JsonNode> ReadOrThrow(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gemini returned {Status}.", (int)response.StatusCode);

            throw new HttpRequestException(
                $"Gemini returned {(int)response.StatusCode}: {Excerpt(raw)}",
                null,
                response.StatusCode);
        }

        return JsonNode.Parse(raw)
               ?? throw new HttpRequestException("Gemini returned an empty body.");
    }

    private static string Excerpt(string body) => body.Length <= 500 ? body : body[..500] + "…";
}

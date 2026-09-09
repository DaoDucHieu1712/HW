using Anthropic;
using HW.Application.Abstractions.AI;
using HW.Application.Abstractions.Chat;
using HW.Application.Abstractions.Development;
using HW.Application.Abstractions.Diagnostics;
using HW.Application.Agents;
using HW.Application.Agents.Queries;
using HW.Application.Features.Vocabs.Agent;
using HW.Infrastructure.Chat;
using HW.Infrastructure.Development;
using HW.Infrastructure.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HW.Infrastructure.AI;

public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// Wires the whole agent stack: the loop, the agents and workflows, the developer tooling, and
    /// one adapter per configured LLM provider.
    ///
    /// <para>
    /// A provider with no API key is skipped rather than registered and left to fail on first use.
    /// That way <see cref="IAgentChatClientResolver.Available"/> is the truth about what this
    /// deployment can call, and a fan-out over three providers on a machine with one key degrades to
    /// one working branch and two clear errors instead of three confusing ones.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAiAgents(this IServiceCollection services, IConfiguration configuration)
    {
        AddDiagnostics(services, configuration);
        AddWorkspace(services, configuration);
        AddChat(services, configuration);
        AddProviders(services, configuration);

        services.AddSingleton<IAgentChatClientResolver, AgentChatClientResolver>();

        services.AddAgentEngine(options =>
        {
            options.MaxIterations = configuration.GetValue("Agents:MaxIterations", options.MaxIterations);
            options.MaxDelegations = configuration.GetValue("Agents:MaxDelegations", options.MaxDelegations);
            options.MaxToolResultChars = configuration.GetValue("Agents:MaxToolResultChars", options.MaxToolResultChars);
            options.MaxToolResultItems = configuration.GetValue("Agents:MaxToolResultItems", options.MaxToolResultItems);
        });

        services.AddVocabAgentCore();

        return services;
    }

    private static void AddProviders(IServiceCollection services, IConfiguration configuration)
    {
        AddClaude(services, configuration);
        AddOpenAi(services, configuration);
        AddGemini(services, configuration);
    }

    private static void AddClaude(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnthropicAgentOptions>()
            .Bind(configuration.GetSection("Anthropic"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The Anthropic SDK reads ANTHROPIC_API_KEY itself, so an empty configured key is not
        // evidence the provider is unavailable — unlike the two HTTP adapters below.
        var configured = configuration["Anthropic:ApiKey"];
        var fromEnvironment = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (string.IsNullOrWhiteSpace(configured) && string.IsNullOrWhiteSpace(fromEnvironment)) return;

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AnthropicAgentOptions>>().Value;

            return string.IsNullOrWhiteSpace(options.ApiKey)
                ? new AnthropicClient()
                : new AnthropicClient { ApiKey = options.ApiKey };
        });

        services.AddScoped<IAgentChatClient, AnthropicAgentChatClient>();
    }

    private static void AddOpenAi(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("OpenAI");

        services.AddOptions<OpenAiAgentOptions>()
            .Bind(section)
            .PostConfigure(options =>
                options.ApiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
            .ValidateDataAnnotations();

        if (!HasKey(section["ApiKey"], "OPENAI_API_KEY")) return;

        // A typed HttpClient: the factory owns the handler's lifetime, which is what keeps a
        // long-lived client from pinning stale DNS.
        services.AddHttpClient<OpenAiAgentChatClient>();
        services.AddScoped<IAgentChatClient>(provider => provider.GetRequiredService<OpenAiAgentChatClient>());
    }

    private static void AddGemini(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Gemini");

        services.AddOptions<GeminiAgentOptions>()
            .Bind(section)
            .PostConfigure(options =>
                options.ApiKey ??= Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
            .ValidateDataAnnotations();

        if (!HasKey(section["ApiKey"], "GEMINI_API_KEY")) return;

        services.AddHttpClient<GeminiAgentChatClient>();
        services.AddScoped<IAgentChatClient>(provider => provider.GetRequiredService<GeminiAgentChatClient>());
    }

    private static bool HasKey(string? configured, string environmentVariable)
        => !string.IsNullOrWhiteSpace(configured)
           || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable));

    /// <summary>
    /// The SQL and log buffers, plus the logging provider that fills the second one.
    ///
    /// <para>
    /// Both stores are singletons: they outlive requests by design, since the point is to still hold
    /// the failing request's trace when someone asks about it afterwards.
    /// </para>
    /// </summary>
    private static void AddDiagnostics(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Diagnostics").Get<DiagnosticsOptions>() ?? new DiagnosticsOptions();
        services.AddSingleton(options);

        services.AddHttpContextAccessor();
        services.AddSingleton<ICorrelationAccessor, HttpCorrelationAccessor>();

        services.AddSingleton(new SqlTraceStore(options.SqlCapacity));
        services.AddSingleton<ISqlTraceStore>(provider => provider.GetRequiredService<SqlTraceStore>());

        services.AddSingleton(new LogTraceStore(options.LogCapacity));
        services.AddSingleton<ILogTraceStore>(provider => provider.GetRequiredService<LogTraceStore>());

        services.AddSingleton<SqlTraceInterceptor>();

        if (!options.Enabled) return;

        services.AddSingleton<ILoggerProvider, TraceLoggerProvider>();
    }

    /// <summary>
    /// The chat endpoint's conversation memory, and which agent it talks to by default.
    /// </summary>
    private static void AddChat(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
        services.AddSingleton(options);

        // A singleton, because a conversation has to outlive the request that started it — that is
        // the whole point of not making the client resend its history.
        services.AddSingleton<IConversationStore, ConversationStore>();
        services.AddSingleton(new ChatDefaults { Agent = options.DefaultAgent });
    }

    private static void AddWorkspace(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Workspace").Get<WorkspaceOptions>() ?? new WorkspaceOptions();
        services.AddSingleton(options);

        services.AddSingleton<IWorkspaceReader, WorkspaceReader>();
        services.AddSingleton<IPatchProposalStore, PatchProposalStore>();
        services.AddSingleton<IPatchApplier, PatchApplier>();
    }
}

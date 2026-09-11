using Anthropic;
using HW.Agentic.Abstractions;
using HW.Agentic.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HW.Agentic.Core;

public static class AgentProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers one adapter per configured LLM provider, plus the resolver that picks between them.
    ///
    /// <para>
    /// A provider with no API key is skipped rather than registered and left to fail on first use.
    /// That way <see cref="IAgentChatClientResolver.Available"/> is the truth about what this
    /// deployment can call, and a fan-out over three providers on a machine with one key degrades to
    /// one working branch and two clear errors instead of three confusing ones.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Configuration sections read: <c>Anthropic</c>, <c>OpenAI</c>, <c>Gemini</c>. Each also accepts
    /// its vendor's conventional environment variable, so a developer machine needs no appsettings
    /// entry at all.
    /// </remarks>
    public static IServiceCollection AddAgentProviders(
        this IServiceCollection services, IConfiguration configuration)
    {
        AddClaude(services, configuration);
        AddOpenAi(services, configuration);
        AddGemini(services, configuration);

        services.AddSingleton<IAgentChatClientResolver, AgentChatClientResolver>();

        return services;
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
}

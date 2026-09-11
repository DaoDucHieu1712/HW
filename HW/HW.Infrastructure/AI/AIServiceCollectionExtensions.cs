using HW.Agentic.Abstractions.Chat;
using HW.Agentic.Abstractions.Development;
using HW.Agentic.Abstractions.Diagnostics;
using HW.Agentic.Core;
using HW.Application.Agents.Queries;
using HW.Application.Features.Vocabs.Agent;
using HW.Infrastructure.Chat;
using HW.Infrastructure.Development;
using HW.Infrastructure.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HW.Infrastructure.AI;

public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// Wires the whole agent stack for this application: the runtime from <c>HW.Agentic</c>, the
    /// host-side implementations its tools read through, and this application's own agents.
    ///
    /// <para>
    /// The split is the point. <c>HW.Agentic</c> knows about loops, tools, and model providers and
    /// nothing about this domain. Everything this method adds around it — where the workspace root
    /// is, which buffer holds the SQL trace, where conversations live, which feature agents exist —
    /// is what makes that generic runtime <i>this</i> application's.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAiAgents(this IServiceCollection services, IConfiguration configuration)
    {
        AddDiagnostics(services, configuration);
        AddWorkspace(services, configuration);
        AddChat(services, configuration);

        services.AddAgentProviders(configuration);

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

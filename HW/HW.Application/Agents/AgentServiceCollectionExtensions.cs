using HW.Application.Abstractions.AI;
using HW.Application.Agents.Tools;
using HW.Application.Agents.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace HW.Application.Agents;

public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the loop, the catalogs, the workflow engine, and the developer tools.
    ///
    /// <para>
    /// The provider adapters behind <see cref="IAgentChatClientResolver"/> are not registered here —
    /// those are infrastructure, and which of them exist depends on which keys are configured.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAgentEngine(
        this IServiceCollection services,
        Action<AgentLoopOptions>? configure = null)
    {
        var options = new AgentLoopOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Scoped: the loop resolves tools, and the tools reach scoped services. The workflow engine
        // is scoped for the same reason, though it creates a child scope per step of its own.
        services.AddScoped<IEngineerLoop, EngineerLoop>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();

        // Catalogs hold immutable definitions and are safe to share.
        services.AddSingleton<IAgentCatalog, AgentCatalog>();
        services.AddSingleton<IWorkflowCatalog, WorkflowCatalog>();

        foreach (var agent in AgentCatalog.Defaults())
            services.AddSingleton(agent);

        foreach (var workflow in WorkflowCatalog.Defaults())
            services.AddSingleton(workflow);

        // Per request, so a manager's delegation budget and its ledger of specialist runs belong to
        // the one question being answered.
        services.AddScoped<IDelegationLedger, DelegationLedger>();
        services.AddScoped<AgentDelegator>();

        services.AddScoped<IAgentTool, ListSpecialistsTool>();
        services.AddScoped<IAgentTool, DelegateTool>();
        services.AddScoped<IAgentTool, DelegateParallelTool>();
        services.AddScoped<IAgentTool, RunWorkflowTool>();

        services.AddScoped<IAgentTool, TraceLogTool>();
        services.AddScoped<IAgentTool, TraceSqlTool>();
        services.AddScoped<IAgentTool, ListFilesTool>();
        services.AddScoped<IAgentTool, ReadFileTool>();
        services.AddScoped<IAgentTool, SearchCodeTool>();
        services.AddScoped<IAgentTool, ProposePatchTool>();

        return services;
    }
}

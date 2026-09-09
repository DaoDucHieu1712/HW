using HW.Application.Abstractions.AI;
using HW.Application.Agents;
using HW.Application.Features.Vocabs.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace HW.Application.Features.Vocabs.Agent;

public static class VocabAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the vocabulary agent's tools and its façade. The loop itself comes from
    /// <see cref="AgentServiceCollectionExtensions.AddAgentEngine"/> — this feature only contributes
    /// tools and an agent definition.
    /// </summary>
    public static IServiceCollection AddVocabAgentCore(this IServiceCollection services)
    {
        // Scoped, like everything else that reaches the database: the tools resolve MediatR handlers
        // that share the request's DbContext.
        services.AddScoped<IVocabAgent, VocabAgentLoop>();

        services.AddScoped<IAgentTool, SearchVocabTool>();
        services.AddScoped<IAgentTool, GetVocabTool>();
        services.AddScoped<IAgentTool, DailyMissionTool>();
        services.AddScoped<IAgentTool, FlashCardsTool>();
        services.AddScoped<IAgentTool, GenerateExamTool>();
        services.AddScoped<IAgentTool, SaveVocabTool>();
        services.AddScoped<IAgentTool, UpdateVocabTool>();
        services.AddScoped<IAgentTool, ReviewVocabTool>();

        return services;
    }
}

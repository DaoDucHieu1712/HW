using HW.Agentic.Abstractions;
using HW.Agentic.Core;
using HW.Application.Features.Vocabs.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace HW.Application.Features.Vocabs.Agent;

public static class VocabAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the vocabulary agent's definition, its tools, and its façade. The loop itself comes
    /// from <see cref="AgentServiceCollectionExtensions.AddAgentEngine"/> — this feature only
    /// contributes what is specific to vocabulary.
    /// </summary>
    public static IServiceCollection AddVocabAgentCore(this IServiceCollection services)
    {
        // The catalog composes every AgentDefinition in the container, so contributing the coach
        // here is what keeps HW.Agentic from having to know this feature exists.
        services.AddSingleton(VocabAgentLoop.Definition());

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

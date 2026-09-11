using System.Reflection;
using HW.Agentic.Abstractions;
using HW.Agentic.Core;
using HW.Agentic.Workflow;
using HW.Application.Features.Vocabs.Agent;

namespace HW.UnitTests.Agentic;

/// <summary>
/// The whole point of pulling the runtime out of <c>HW.Application</c> was that it stops knowing
/// about this application. That property is invisible at a call site and easy to lose to one
/// convenient <c>using</c>, so it is asserted rather than documented.
/// </summary>
public class AgenticIsolationTests
{
    private static readonly Assembly Agentic = typeof(IEngineerLoop).Assembly;

    [Fact]
    public void The_runtime_does_not_reference_the_application_or_the_domain()
    {
        var referenced = Agentic.GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => name.StartsWith("HW.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(referenced);
    }

    [Fact]
    public void The_runtime_carries_no_type_from_a_feature_namespace()
    {
        var leaked = Agentic.GetTypes()
            .Where(type => type.Namespace?.StartsWith("HW.Application", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .ToList();

        Assert.Empty(leaked);
    }

    [Fact]
    public void Every_public_type_lives_under_the_HW_Agentic_root()
    {
        var strays = Agentic.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("HW.Agentic", StringComparison.Ordinal) != true)
            .Select(type => type.FullName!)
            .ToList();

        Assert.Empty(strays);
    }

    [Fact]
    public void The_built_in_specialists_are_all_about_code_and_the_running_system()
    {
        // A feature agent appearing here would mean the catalog had been coupled to a host again.
        Assert.Equal(
            new[] { "bugfixer", "developer", "log-tracer", "manager", "reviewer", "sql-tracer", "synthesizer" },
            AgentCatalog.Defaults().Select(agent => agent.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void A_host_agent_reaches_the_catalog_by_registration_not_by_reference()
    {
        // What HW.Application contributes at startup, composed the way DI would compose it.
        var catalog = new AgentCatalog([.. AgentCatalog.Defaults(), VocabAgentLoop.Definition()]);

        Assert.Equal("vocab-coach", catalog.Get("vocab-coach").Name);
        Assert.Equal("manager", catalog.Get("manager").Name);
    }

    [Fact]
    public void Every_default_specialist_names_only_tools_the_runtime_actually_ships()
    {
        var registered = Agentic.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IAgentTool).IsAssignableFrom(type))
            .Select(NameOf)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var agent in AgentCatalog.Defaults())
            foreach (var tool in agent.Tools)
                Assert.True(registered.Contains(tool), $"agent '{agent.Name}' names unknown tool '{tool}'");
    }

    [Fact]
    public void Every_default_workflow_names_a_registered_agent()
    {
        var agents = AgentCatalog.Defaults().Select(agent => agent.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var workflow in WorkflowCatalog.Defaults())
            foreach (var name in workflow.Steps.SelectMany(AgentsUsedBy))
                Assert.True(agents.Contains(name), $"workflow '{workflow.Name}' names unknown agent '{name}'");
    }

    private static IEnumerable<string> AgentsUsedBy(WorkflowStep step) => step switch
    {
        AgentWorkflowStep single => [single.Agent],
        ParallelWorkflowStep parallel =>
            parallel.Branches.Select(branch => branch.Agent)
                .Concat(parallel.SynthesisAgent is null ? [] : new[] { parallel.SynthesisAgent }),
        _ => [],
    };

    /// <summary>Reads a tool's advertised name off an instance built without its dependencies.</summary>
    private static string NameOf(Type toolType)
    {
        var tool = (IAgentTool)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(toolType);
        return tool.Name;
    }
}

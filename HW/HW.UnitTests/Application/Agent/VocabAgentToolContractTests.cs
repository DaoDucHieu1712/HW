using System.Text.Json;
using HW.Agentic.Abstractions;
using HW.Agentic.Core;
using HW.Application.Features.Vocabs.Agent;
using HW.Application.Features.Vocabs.Agent.Tools;
using HW.UnitTests.TestSupport;

namespace HW.UnitTests.Application.Agent;

/// <summary>
/// The tool surface itself. Names and schemas are what the model sees, and the mutating flag is what
/// decides whether a read-only session is advertised a write tool at all — so all three are pinned
/// here rather than left to be noticed after a rename.
/// </summary>
public class VocabAgentToolContractTests
{
    private static IEnumerable<IAgentTool> AllTools()
    {
        var sender = new StubSender();
        var options = new AgentLoopOptions();

        yield return new SearchVocabTool(sender, options);
        yield return new GetVocabTool(sender, options);
        yield return new DailyMissionTool(sender, options);
        yield return new FlashCardsTool(sender, options);
        yield return new GenerateExamTool(sender, options);
        yield return new SaveVocabTool(sender, options);
        yield return new UpdateVocabTool(sender, options);
        yield return new ReviewVocabTool(sender, options);
    }

    [Fact]
    public void The_agent_advertises_exactly_the_tools_that_exist()
    {
        Assert.Equal(
            VocabAgentLoop.ToolNames.OrderBy(name => name),
            AllTools().Select(tool => tool.Name).OrderBy(name => name));
    }

    [Fact]
    public void The_agent_lists_no_tool_twice()
    {
        Assert.Equal(VocabAgentLoop.ToolNames.Count, VocabAgentLoop.ToolNames.Distinct().Count());
    }

    [Fact]
    public void Every_tool_publishes_a_valid_json_schema()
    {
        foreach (var tool in AllTools())
        {
            using var schema = JsonDocument.Parse(tool.InputSchemaJson);

            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            Assert.True(schema.RootElement.TryGetProperty("properties", out _), $"{tool.Name} declares no properties");
        }
    }

    [Fact]
    public void Every_tool_describes_itself_to_the_model()
    {
        foreach (var tool in AllTools())
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        }
    }

    [Fact]
    public void Every_required_argument_is_a_declared_property()
    {
        foreach (var tool in AllTools())
        {
            using var schema = JsonDocument.Parse(tool.InputSchemaJson);

            if (!schema.RootElement.TryGetProperty("required", out var required)) continue;

            var properties = schema.RootElement.GetProperty("properties");

            foreach (var name in required.EnumerateArray().Select(x => x.GetString()!))
                Assert.True(properties.TryGetProperty(name, out _), $"{tool.Name} requires undeclared '{name}'");
        }
    }

    [Theory]
    [InlineData("save_vocab")]
    [InlineData("update_vocab")]
    [InlineData("mark_vocab_reviewed")]
    public void The_writing_tools_are_flagged_as_mutating(string name)
    {
        Assert.True(AllTools().Single(tool => tool.Name == name).IsMutating);
    }

    [Theory]
    [InlineData("search_vocab")]
    [InlineData("get_vocab")]
    [InlineData("daily_mission")]
    [InlineData("draw_flashcards")]
    [InlineData("generate_exam")]
    public void The_reading_tools_are_not(string name)
    {
        Assert.False(AllTools().Single(tool => tool.Name == name).IsMutating);
    }

    [Fact]
    public void The_definition_names_the_vocab_coach_and_its_tools()
    {
        var definition = VocabAgentLoop.Definition();

        Assert.Equal("vocab-coach", definition.Name);
        Assert.Equal(LlmProvider.Claude, definition.Provider);
        Assert.Equal(VocabAgentLoop.ToolNames, definition.Tools);
        Assert.False(string.IsNullOrWhiteSpace(definition.SystemPrompt));
    }

    [Fact]
    public void Web_lookup_is_off_unless_the_request_asks_for_it()
    {
        Assert.False(VocabAgentLoop.Definition().EnableWebLookup);
        Assert.True(VocabAgentLoop.Definition(enableWebLookup: true).EnableWebLookup);
    }
}

# HW.Agentic

The agent runtime, extracted from `HW.Application` so that it knows about loops, tools, and model
providers — and nothing about this application's domain.

```
Abstractions/   IAgentChatClient · IAgentTool · AgentModels · LlmProvider
                Development/ (workspace, patch proposals) · Diagnostics/ (trace stores) · Chat/
Core/           EngineerLoop · AgentDefinition · AgentCatalog · AgentLoopOptions · AgentRunModels
Delegation/     AgentDelegator · DelegationLedger · list_specialists / delegate / delegate_parallel / run_workflow
Workflow/       WorkflowEngine · WorkflowCatalog · WorkflowDefinition
Tools/          AgentToolBase · trace_log · trace_sql · list_files / read_file / search_code · propose_patch
Providers/      Anthropic · OpenAI · Gemini adapters, the resolver, and their options
Prompts/        ManagerPrompt · DeveloperAgentPrompts
DI/             AddAgentEngine() · AddAgentProviders()
```

It references no other project in the solution. `HW.UnitTests/Agentic/AgenticIsolationTests.cs`
asserts that, because the property is invisible at a call site and one convenient `using` would
lose it.

## The three seams

**`IAgentChatClient`** is the only place a vendor is named. Everything above it is
provider-neutral, which is what lets one workflow put the same question to Claude, GPT and Gemini
at once and reconcile the answers.

**`IAgentTool`** is what an agent can do. Tools live next to whatever they expose — the ones here
are about code and the running system, so they ship with the runtime; a feature's tools belong to
that feature.

**The abstractions under `Abstractions/Development`, `Abstractions/Diagnostics` and
`Abstractions/Chat`** are what the built-in tools read through. The runtime declares them; the host
decides what a "workspace" or a "trace store" actually is. `HW.Infrastructure` supplies this
application's answers.

## Using it

```csharp
services.AddAgentProviders(configuration);   // one adapter per configured API key
services.AddAgentEngine(options => { ... }); // loop, catalogs, workflow engine, dev tools
```

`AddAgentProviders` skips a provider with no API key rather than registering it and letting it fail
on first use, so `IAgentChatClientResolver.Available` is the truth about what a deployment can
actually call. Sections read: `Anthropic`, `OpenAI`, `Gemini`; each also accepts its vendor's
conventional environment variable.

### Adding an agent

An agent is data, not a class. Register a definition and the catalog picks it up:

```csharp
services.AddSingleton(new AgentDefinition(
    Name: "my-specialist",
    Description: "...",           // read by the manager when it decides who to delegate to
    SystemPrompt: MyPrompts.System,
    Tools: ["search_x", "read_x"], // an EMPTY list means no tools, not every tool
    Provider: LlmProvider.Claude));
```

`HW.Application/Features/Vocabs/Agent/VocabAgentServiceCollectionExtensions.cs` is the worked
example. Do not add a host's agent to `AgentCatalog.Defaults()` — that is the direction that made
the runtime depend on the application it runs inside, and is exactly what this project undid.

### Adding a tool

Derive from `AgentToolBase` (it owns the lenient argument readers — model-generated JSON arrives
with numbers quoted and optionals null rather than absent) and register it as `IAgentTool`. Set
`IsMutating` on anything that writes: a read-only run never sees those tools advertised at all.

## What the loop guarantees

Every way out is bounded. The iteration cap stops a model that keeps reaching for tools; a failing
tool comes back as an error result the model can correct rather than an exception that kills the
run; and an exhausted budget still produces an answer, from one final call made with tool use
withheld.

One run stays on one provider. Assistant turns are replayed through the provider's own opaque
content blocks — Claude's thinking blocks carry a signature the API rejects if altered, and
OpenAI's `tool_calls` must come back verbatim for results to correlate — so a fan-out gives each
provider its own fresh conversation rather than switching mid-conversation.

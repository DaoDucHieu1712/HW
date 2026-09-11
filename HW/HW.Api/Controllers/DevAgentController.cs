using System.Security.Cryptography;
using System.Text;
using HW.Api.Models;
using HW.Agentic.Abstractions;
using HW.Agentic.Abstractions.Development;
using HW.Agentic.Core;
using HW.Application.Agents.Queries;
using HW.Agentic.Tools;
using HW.Agentic.Workflow;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Agents.Dtos.AgentDtos;

namespace HW.Api.Controllers;

/// <summary>
/// The multi-agent developer assistant: specialists that read this application's logs, its SQL, and
/// its source, and workflows that combine them — including fanning one question out across Claude,
/// GPT, and Gemini at once.
/// </summary>
/// <remarks>
/// Nothing here writes to the source tree. An agent's edits arrive as a patch proposal and stay in
/// the queue until someone approves one through this controller.
/// </remarks>
[Route("api/dev-agent")]
[ApiController]
public class DevAgentController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IAgentCatalog _agents;
    private readonly IWorkflowCatalog _workflows;
    private readonly IAgentChatClientResolver _providers;
    private readonly IPatchProposalStore _patches;
    private readonly IPatchApplier _applier;
    private readonly IWorkspaceReader _workspace;

    public DevAgentController(
        ISender sender,
        IAgentCatalog agents,
        IWorkflowCatalog workflows,
        IAgentChatClientResolver providers,
        IPatchProposalStore patches,
        IPatchApplier applier,
        IWorkspaceReader workspace)
    {
        _sender = sender;
        _agents = agents;
        _workflows = workflows;
        _providers = providers;
        _patches = patches;
        _applier = applier;
        _workspace = workspace;
    }

    /// <summary>Lists the registered agents, with the provider and tools each one runs with.</summary>
    [HttpGet("agents")]
    public IActionResult GetAgents()
    {
        var agents = _agents.All
            .Select(agent => new AgentSummaryDto(
                agent.Name, agent.Description, agent.Provider.ToString(), agent.Model, agent.Tools, agent.MaxIterations))
            .ToList();

        return Ok(ApiResponseFactory.Success(agents));
    }

    /// <summary>Lists the registered workflows and the shape of each one's steps.</summary>
    [HttpGet("workflows")]
    public IActionResult GetWorkflows()
    {
        var workflows = _workflows.All
            .Select(workflow => new WorkflowSummaryDto(
                workflow.Name,
                workflow.Description,
                workflow.Steps.Select(Describe).ToList()))
            .ToList();

        return Ok(ApiResponseFactory.Success(workflows));
    }

    /// <summary>
    /// Which providers this deployment can actually call. A provider is unavailable when its API key
    /// is not configured — check here before running a fan-out that expects all three.
    /// </summary>
    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        var available = _providers.Available.Select(provider => provider.ToString()).ToList();

        var unavailable = Enum.GetNames<LlmProvider>()
            .Except(available, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(ApiResponseFactory.Success(new ProvidersDto(available, unavailable)));
    }

    /// <summary>
    /// Runs one agent against one task. The response carries the answer plus every tool call it made,
    /// so its reasoning can be checked rather than taken on trust.
    /// </summary>
    /// <remarks>
    /// Set <c>provider</c> to run the same agent on a different vendor. Send prior turns in
    /// <c>history</c> to continue a conversation — nothing is stored between calls.
    /// </remarks>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskAgentRequestDto dto, CancellationToken ct)
    {
        var result = await _sender.Send(new AskAgentQuery(dto), ct);
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>
    /// Runs a workflow end to end. Parallel steps fan out concurrently, and a step that fails is
    /// recorded and stepped over rather than ending the run — read <c>steps[].failed</c> to see.
    /// </summary>
    [HttpPost("workflows/{name}/run")]
    public async Task<IActionResult> RunWorkflow(string name, [FromBody] RunWorkflowRequestDto dto, CancellationToken ct)
    {
        var result = await _sender.Send(new RunWorkflowQuery(name, dto.Input), ct);
        return Ok(ApiResponseFactory.Success(result));
    }

    /// <summary>The patch queue. Proposals wait here until approved or rejected; nothing is on disk yet.</summary>
    [HttpGet("patches")]
    public IActionResult GetPatches([FromQuery] PatchStatus? status, [FromQuery] int limit = 20)
        => Ok(ApiResponseFactory.Success(_patches.List(status, limit)));

    /// <summary>One proposal in full, including each file's new contents and a diff to read.</summary>
    [HttpGet("patches/{id}")]
    public IActionResult GetPatch(string id)
    {
        var proposal = _patches.Get(id);

        return proposal is null
            ? NotFound(ApiResponseFactory.NotFound<object>($"No patch proposal with id '{id}'."))
            : Ok(ApiResponseFactory.Success(proposal));
    }

    /// <summary>
    /// Queues a patch proposed from outside the in-process agents.
    /// </summary>
    /// <remarks>
    /// This is how the LangGraph loop in <c>agentic/</c> hands over a change it has already built,
    /// tested and reviewed in its own git worktree. It shares this queue deliberately: one place
    /// where pending edits are visible, one approval path, one staleness check. Like every other
    /// proposal, nothing is written until someone approves it — the proposer being a different
    /// process buys it no additional trust.
    ///
    /// <para>
    /// The hash recorded per file is of the file as it stands in <i>this</i> workspace right now,
    /// not as the loop saw it in its worktree. That is what makes the staleness check meaningful
    /// here: it catches the file being edited between this call and the approval.
    /// </para>
    /// </remarks>
    [HttpPost("patches")]
    public async Task<IActionResult> CreatePatch([FromBody] CreatePatchRequestDto dto, CancellationToken ct)
    {
        if (dto.Files is null || dto.Files.Count == 0)
            return BadRequest(ApiResponseFactory.Error<object>("Send at least one file to change."));

        var files = new List<PatchFile>(dto.Files.Count);

        foreach (var file in dto.Files)
        {
            if (string.IsNullOrEmpty(file.NewContent))
            {
                return BadRequest(ApiResponseFactory.Error<object>(
                    $"File '{file.Path}' has empty content. Send the complete file, not a fragment."));
            }

            var original = _workspace.Exists(file.Path)
                ? await _workspace.ReadRawAsync(file.Path, ct)
                : null;

            files.Add(new PatchFile(
                file.Path,
                original is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))),
                file.NewContent,
                UnifiedDiff.Build(file.Path, original, file.NewContent)));
        }

        var proposal = _patches.Add(dto.Agent, dto.Title, dto.Rationale, files);

        return CreatedAtAction(nameof(GetPatch), new { id = proposal.Id }, ApiResponseFactory.Created(proposal));
    }

    /// <summary>
    /// Approves a proposal and writes it to disk.
    /// </summary>
    /// <remarks>
    /// This is the only endpoint in the system that changes source files. A proposal whose files
    /// have been edited since the agent read them comes back <c>Stale</c> and nothing is written —
    /// re-run the agent against the current code rather than forcing it through.
    /// </remarks>
    [HttpPost("patches/{id}/approve")]
    public async Task<IActionResult> ApprovePatch(string id, CancellationToken ct)
    {
        var result = await _applier.ApproveAsync(id, User.Identity?.Name ?? "anonymous", ct);
        return Ok(ApiResponseFactory.Success(result));
    }

    [HttpPost("patches/{id}/reject")]
    public IActionResult RejectPatch(string id, [FromBody] RejectPatchRequestDto? dto)
        => Ok(ApiResponseFactory.Success(
            _applier.Reject(id, User.Identity?.Name ?? "anonymous", dto?.Reason)));

    private static WorkflowStepSummaryDto Describe(WorkflowStep step) => step switch
    {
        AgentWorkflowStep agent => new(step.Id, "agent", agent.Agent, null, null),

        ParallelWorkflowStep parallel => new(
            step.Id,
            "parallel",
            null,
            parallel.Branches
                .Select(branch => $"{branch.Agent} @ {branch.ProviderOverride?.ToString() ?? "default"}")
                .ToList(),
            parallel.SynthesisAgent),

        _ => new(step.Id, "unknown", null, null, null),
    };
}

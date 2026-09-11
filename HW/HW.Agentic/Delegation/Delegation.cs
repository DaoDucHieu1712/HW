using System.Diagnostics;
using HW.Agentic.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HW.Agentic.Core;

/// <summary>One specialist run that a manager asked for, kept so the caller can see the work behind an answer.</summary>
public sealed record DelegationRecord(
    string Agent,
    LlmProvider Provider,
    string Task,
    string Answer,
    bool Failed,
    string? Error,
    int Iterations,
    bool BudgetExhausted,
    IReadOnlyList<AgentStep> Steps,
    AgentUsage Usage,
    long ElapsedMs);

/// <summary>
/// Collects the delegations made while answering one request.
///
/// <para>
/// It exists because a manager's own answer is a summary: without the ledger, the specialist runs
/// underneath it — the tool calls, the SQL each one read, the tokens they cost — would vanish, and
/// the manager's account of them would be unverifiable. Registered per request, and written to from
/// several threads at once when delegations run in parallel.
/// </para>
/// </summary>
public interface IDelegationLedger
{
    IReadOnlyList<DelegationRecord> Entries { get; }

    int Count { get; }

    void Record(DelegationRecord record);
}

public sealed class DelegationLedger : IDelegationLedger
{
    private readonly List<DelegationRecord> _entries = [];
    private readonly object _gate = new();

    public IReadOnlyList<DelegationRecord> Entries
    {
        get { lock (_gate) return [.. _entries]; }
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public void Record(DelegationRecord record)
    {
        lock (_gate) _entries.Add(record);
    }
}

/// <summary>
/// Runs a specialist on a manager's behalf.
///
/// <para>
/// Three things are enforced here rather than in the manager's prompt, because a prompt is a request
/// and this is a guarantee: an orchestrator cannot be delegated to (which is what bounds recursion
/// to one level, structurally, with no depth counter to get wrong); the number of delegations per
/// request is capped; and a specialist that fails comes back as a readable failure rather than
/// taking the manager's run down with it.
/// </para>
/// </summary>
public sealed class AgentDelegator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentCatalog _catalog;
    private readonly IDelegationLedger _ledger;
    private readonly AgentLoopOptions _options;
    private readonly ILogger<AgentDelegator> _logger;

    public AgentDelegator(
        IServiceScopeFactory scopeFactory,
        IAgentCatalog catalog,
        IDelegationLedger ledger,
        AgentLoopOptions options,
        ILogger<AgentDelegator> logger)
    {
        _scopeFactory = scopeFactory;
        _catalog = catalog;
        _ledger = ledger;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Agents a manager may hand work to: everything that is not itself an orchestrator.
    /// </summary>
    public IReadOnlyList<AgentDefinition> Delegatable()
        => _catalog.All.Where(agent => !IsOrchestrator(agent)).ToList();

    /// <summary>
    /// An agent holding any delegation tool orchestrates rather than works. Deciding this from the
    /// tool list rather than from a name means a second manager added later is excluded too,
    /// without anyone remembering to update a deny-list.
    /// </summary>
    public static bool IsOrchestrator(AgentDefinition agent)
        => agent.Tools.Any(DelegationToolNames.Contains);

    public static readonly IReadOnlySet<string> DelegationToolNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "delegate",
            "delegate_parallel",
            "run_workflow",
        };

    public async Task<DelegationRecord> RunAsync(
        string agentName,
        string task,
        LlmProvider? provider,
        bool allowMutations,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        if (_ledger.Count >= _options.MaxDelegations)
        {
            // The manager's own iteration cap does not bound this: one turn can ask for several
            // delegations at once, and each is a whole nested agent run.
            return Failed(agentName, provider, task,
                $"Delegation budget spent ({_options.MaxDelegations} for this request). " +
                "Answer from what the specialists have already reported.", started);
        }

        if (!_catalog.TryGet(agentName, out var agent))
        {
            var names = string.Join(", ", Delegatable().Select(candidate => candidate.Name));
            return Failed(agentName, provider, task, $"No agent named '{agentName}'. Available: {names}.", started);
        }

        if (IsOrchestrator(agent))
        {
            return Failed(agentName, provider, task,
                $"'{agent.Name}' coordinates other agents and cannot be delegated to. " +
                "Hand the work to a specialist instead.", started);
        }

        try
        {
            // Its own scope: the nested run resolves its own tools and its own DbContext, so several
            // delegations can be in flight at once without sharing either.
            using var scope = _scopeFactory.CreateScope();
            var loop = scope.ServiceProvider.GetRequiredService<IEngineerLoop>();

            var result = await loop.RunAsync(
                new AgentRunRequest(agent, task, History: null, allowMutations, provider),
                ct);

            var record = new DelegationRecord(
                result.Agent, result.Provider, task, result.Answer,
                Failed: false, Error: null,
                result.Iterations, result.BudgetExhausted, result.Steps, result.Usage, result.ElapsedMs);

            _ledger.Record(record);
            return record;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller walked away. Abandon everything rather than letting the manager carry on
            // spending on further delegations.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delegation to {Agent} failed.", agentName);
            return Failed(agentName, provider ?? agent.Provider, task, ex.Message, started);
        }
    }

    private DelegationRecord Failed(
        string agentName, LlmProvider? provider, string task, string error, long started)
    {
        var record = new DelegationRecord(
            agentName, provider ?? LlmProvider.Claude, task,
            Answer: error, Failed: true, Error: error,
            Iterations: 0, BudgetExhausted: false, Steps: [], Usage: AgentUsage.Zero,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        _ledger.Record(record);
        return record;
    }
}

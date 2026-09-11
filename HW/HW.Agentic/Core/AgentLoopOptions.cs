namespace HW.Agentic.Core;

/// <summary>Budgets and guard rails shared by every agent the <see cref="EngineerLoop"/> runs.</summary>
public sealed class AgentLoopOptions
{
    /// <summary>
    /// How many model round-trips one question may cost. This is the loop's stop condition: without
    /// it a model that keeps asking for tools runs until the request times out or the bill notices.
    /// Ten leaves room for look up → read the list → save → confirm, with slack for a retry.
    /// </summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>
    /// Ceiling on a single tool result, in characters. A tool that dumps the whole vocab table would
    /// otherwise push the useful context out on the next turn — and every turn re-sends it.
    /// </summary>
    public int MaxToolResultChars { get; set; } = 8_000;

    /// <summary>
    /// Specialist runs a manager may commission while answering one request.
    ///
    /// <para>
    /// Separate from <see cref="MaxIterations"/> because it bounds something else: one manager turn
    /// can ask for several delegations at once, and each of those is a whole nested agent run with
    /// its own iteration budget. Eight covers trace-two-things, read the code, propose, review.
    /// </para>
    /// </summary>
    public int MaxDelegations { get; set; } = 8;

    /// <summary>
    /// Rows a search or listing tool may return in one call. Keeps the model paging deliberately
    /// instead of pulling the table in and reasoning over it.
    /// </summary>
    public int MaxToolResultItems { get; set; } = 25;
}

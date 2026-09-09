namespace HW.Application.Abstractions.Development;

public enum PatchStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,

    /// <summary>
    /// Approved, but the file had changed underneath it since the proposal was made. Nothing was
    /// written — re-run the agent against the current file rather than forcing it through.
    /// </summary>
    Stale = 3,

    /// <summary>Approved, and writing it failed. The error says why; nothing partial was left behind.</summary>
    Failed = 4
}

/// <param name="OriginalHash">
/// SHA-256 of the file as the agent read it. Checked again at approval: an agent's patch is only
/// meaningful against the text it saw, and a file edited in between makes the patch a silent
/// overwrite of someone else's work.
/// </param>
/// <param name="NewContent">
/// The full intended contents. Whole-file replacement rather than a unified diff on purpose — an
/// applied diff has to be parsed and matched with fuzz, and a model that miscounts context lines
/// then produces a patch that applies cleanly in the wrong place. The diff is still generated, but
/// for a human to read, not for the machine to apply.
/// </param>
public sealed record PatchFile(string Path, string? OriginalHash, string NewContent, string Diff);

public sealed record PatchProposal(
    string Id,
    string Agent,
    string Title,
    string Rationale,
    IReadOnlyList<PatchFile> Files,
    PatchStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    string? Message);

/// <summary>
/// Where an agent's proposed edits wait for a human.
///
/// <para>
/// This is the approval gate: the developer agents can write here and nowhere else, and nothing
/// reaches the disk until <see cref="IPatchApplier"/> is called from the approve endpoint. The
/// separation is the safety property — a compromised or confused prompt can at worst fill this
/// store with proposals nobody accepts.
/// </para>
/// </summary>
public interface IPatchProposalStore
{
    PatchProposal Add(string agent, string title, string rationale, IReadOnlyList<PatchFile> files);

    IReadOnlyList<PatchProposal> List(PatchStatus? status, int limit);

    PatchProposal? Get(string id);

    PatchProposal Update(PatchProposal proposal);
}

public interface IPatchApplier
{
    /// <summary>
    /// Writes an approved proposal to disk, refusing any file whose hash no longer matches what the
    /// agent read. All-or-nothing: the hashes are checked for every file before the first write.
    /// </summary>
    Task<PatchProposal> ApproveAsync(string id, string approvedBy, CancellationToken ct = default);

    PatchProposal Reject(string id, string rejectedBy, string? reason);
}

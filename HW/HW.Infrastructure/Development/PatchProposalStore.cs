using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HW.Application.Abstractions.Development;
using Microsoft.Extensions.Logging;

namespace HW.Infrastructure.Development;

/// <summary>
/// Holds proposed edits until a human decides on them.
///
/// <para>
/// In process memory, and lost on restart. That is the right trade for a review queue whose items
/// are meant to be looked at within minutes: persisting them would mean a proposal outliving the
/// code it was written against, which is the one state this design works hardest to avoid.
/// </para>
/// </summary>
public sealed class PatchProposalStore : IPatchProposalStore
{
    private readonly ConcurrentDictionary<string, PatchProposal> _proposals = new(StringComparer.Ordinal);

    public PatchProposal Add(string agent, string title, string rationale, IReadOnlyList<PatchFile> files)
    {
        var proposal = new PatchProposal(
            Id: Guid.NewGuid().ToString("N")[..12],
            agent,
            title,
            rationale,
            files,
            PatchStatus.Pending,
            DateTimeOffset.UtcNow,
            DecidedAt: null,
            DecidedBy: null,
            Message: null);

        _proposals[proposal.Id] = proposal;
        return proposal;
    }

    public PatchProposal? Get(string id) => _proposals.GetValueOrDefault(id);

    public IReadOnlyList<PatchProposal> List(PatchStatus? status, int limit)
        => _proposals.Values
            .Where(proposal => status is null || proposal.Status == status)
            .OrderByDescending(proposal => proposal.CreatedAt)
            .Take(Math.Max(limit, 1))
            .ToList();

    public PatchProposal Update(PatchProposal proposal)
    {
        _proposals[proposal.Id] = proposal;
        return proposal;
    }
}

/// <summary>
/// The approval gate. This is the only code in the system that writes an agent's work to disk, and
/// it is reachable only from the approve endpoint — never from a tool.
/// </summary>
public sealed class PatchApplier : IPatchApplier
{
    private readonly IPatchProposalStore _store;
    private readonly WorkspaceOptions _options;
    private readonly IWorkspaceReader _workspace;
    private readonly ILogger<PatchApplier> _logger;

    public PatchApplier(
        IPatchProposalStore store,
        IWorkspaceReader workspace,
        WorkspaceOptions options,
        ILogger<PatchApplier> logger)
    {
        _store = store;
        _workspace = workspace;
        _options = options;
        _logger = logger;
    }

    public async Task<PatchProposal> ApproveAsync(string id, string approvedBy, CancellationToken ct = default)
    {
        var proposal = _store.Get(id)
            ?? throw new KeyNotFoundException($"No patch proposal with id '{id}'.");

        if (proposal.Status != PatchStatus.Pending)
            throw new InvalidOperationException($"Proposal '{id}' was already {proposal.Status}.");

        // Every file is checked before any is written. A patch touching three files that turns out
        // to be stale on the third must not leave the first two applied.
        foreach (var file in proposal.Files)
        {
            var exists = _workspace.Exists(file.Path);

            if (file.OriginalHash is null && exists)
                return Decide(proposal, PatchStatus.Stale, approvedBy, $"'{file.Path}' was created since the proposal was made.");

            if (file.OriginalHash is null) continue;

            if (!exists)
                return Decide(proposal, PatchStatus.Stale, approvedBy, $"'{file.Path}' no longer exists.");

            var current = Hash(await _workspace.ReadRawAsync(file.Path, ct));

            if (!string.Equals(current, file.OriginalHash, StringComparison.OrdinalIgnoreCase))
            {
                // The agent wrote this patch against text that has since changed. Applying it would
                // silently revert whoever made that change.
                return Decide(
                    proposal, PatchStatus.Stale, approvedBy,
                    $"'{file.Path}' has changed since the agent read it. Re-run the agent against the current file.");
            }
        }

        try
        {
            foreach (var file in proposal.Files)
            {
                var absolute = ResolveForWrite(file.Path);

                Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
                await File.WriteAllTextAsync(absolute, file.NewContent, ct);

                _logger.LogWarning(
                    "Patch {Proposal} approved by {ApprovedBy} wrote {Path}.", proposal.Id, approvedBy, file.Path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying patch {Proposal} failed.", proposal.Id);
            return Decide(proposal, PatchStatus.Failed, approvedBy, ex.Message);
        }

        return Decide(proposal, PatchStatus.Approved, approvedBy, $"Wrote {proposal.Files.Count} file(s).");
    }

    public PatchProposal Reject(string id, string rejectedBy, string? reason)
    {
        var proposal = _store.Get(id)
            ?? throw new KeyNotFoundException($"No patch proposal with id '{id}'.");

        if (proposal.Status != PatchStatus.Pending)
            throw new InvalidOperationException($"Proposal '{id}' was already {proposal.Status}.");

        return Decide(proposal, PatchStatus.Rejected, rejectedBy, reason);
    }

    /// <summary>
    /// Re-runs the root check on the write path. <see cref="IWorkspaceReader"/> validated it on the
    /// way in, but a write is worth proving safe against the same boundary a second time rather
    /// than trusting a value that has been sitting in a queue.
    /// </summary>
    private string ResolveForWrite(string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(_workspace.Root);
        var combined = Path.GetFullPath(Path.Combine(root, relativePath.Replace('\\', '/')));

        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"'{relativePath}' is outside the repository root.");

        var extension = Path.GetExtension(combined);

        if (!_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"'{relativePath}' is not a writable source file.");

        return combined;
    }

    private PatchProposal Decide(PatchProposal proposal, PatchStatus status, string by, string? message)
        => _store.Update(proposal with
        {
            Status = status,
            DecidedAt = DateTimeOffset.UtcNow,
            DecidedBy = by,
            Message = message,
        });

    private static string Hash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}

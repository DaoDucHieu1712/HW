namespace HW.Application.Abstractions.Development;

public sealed record WorkspaceFile(string Path, int Lines, long Bytes, DateTimeOffset ModifiedAt);

public sealed record CodeMatch(string Path, int Line, string Text);

/// <summary>
/// Read access to the source tree, for the agents that reason about code.
///
/// <para>
/// Every path is relative to one configured root and validated against it. That check is the whole
/// point of the abstraction: a path arriving here was written by a language model, and
/// <c>../../../etc/passwd</c> is a thing a model will produce when it is confused about where it is.
/// </para>
/// </summary>
public interface IWorkspaceReader
{
    /// <summary>Absolute path of the root. Shown to agents so they know what paths are relative to.</summary>
    string Root { get; }

    IReadOnlyList<WorkspaceFile> List(string? globPattern, int limit);

    /// <summary>
    /// Reads a file, optionally a line window of it. Line numbers are 1-based and are prefixed onto
    /// each line, because an agent that cannot cite a line cannot propose a patch against it.
    /// </summary>
    Task<string> ReadAsync(string relativePath, int? fromLine, int? toLine, CancellationToken ct = default);

    /// <summary>Raw content, with no line numbers — what a patch proposal is diffed against.</summary>
    Task<string> ReadRawAsync(string relativePath, CancellationToken ct = default);

    Task<IReadOnlyList<CodeMatch>> SearchAsync(
        string pattern,
        string? globPattern,
        bool isRegex,
        int limit,
        CancellationToken ct = default);

    bool Exists(string relativePath);
}

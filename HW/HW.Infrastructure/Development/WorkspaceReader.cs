using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using HW.Agentic.Abstractions.Development;

namespace HW.Infrastructure.Development;

public sealed class WorkspaceOptions
{
    /// <summary>
    /// Repository root. Left empty it is discovered by walking up from the running binary looking
    /// for a solution file, which is what makes this work unchanged from an IDE, from
    /// <c>dotnet run</c>, and from a published folder next to the source.
    /// </summary>
    public string? Root { get; set; }

    /// <summary>Files larger than this are refused rather than truncated — a truncated source file reads as a broken one.</summary>
    [Range(1_024, 5_000_000)]
    public int MaxFileBytes { get; set; } = 400_000;

    /// <summary>Directory names never listed, read, or searched.</summary>
    public string[] ExcludedDirectories { get; set; } =
        ["bin", "obj", ".git", ".vs", "node_modules", "TestResults", ".idea"];

    /// <summary>
    /// Extensions the agents may read. An allowlist rather than a blocklist: the failure mode of
    /// guessing wrong is handing a model a megabyte of binary noise, or a secret in a file type
    /// nobody thought to exclude.
    /// </summary>
    public string[] AllowedExtensions { get; set; } =
    [
        ".cs", ".csproj", ".slnx", ".sln", ".json", ".md", ".yml", ".yaml", ".xml",
        ".props", ".targets", ".editorconfig", ".sql", ".http", ".razor", ".cshtml",
    ];
}

/// <summary>
/// Read-only access to the source tree for the code-reading agents.
///
/// <para>
/// Every public method funnels through <see cref="Resolve"/>, which is the security boundary: paths
/// arriving here were written by a language model, and one that is confused about where it is will
/// happily ask for <c>../../../../etc/passwd</c>. Resolution is done with a full-path comparison
/// against the root rather than by inspecting the string for <c>..</c>, because string inspection is
/// what gets bypassed.
/// </para>
/// </summary>
public sealed class WorkspaceReader : IWorkspaceReader
{
    private readonly WorkspaceOptions _options;
    private readonly HashSet<string> _excluded;
    private readonly HashSet<string> _allowedExtensions;

    public WorkspaceReader(WorkspaceOptions options)
    {
        _options = options;
        Root = ResolveRoot(options.Root);
        _excluded = new HashSet<string>(options.ExcludedDirectories, StringComparer.OrdinalIgnoreCase);
        _allowedExtensions = new HashSet<string>(options.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    public string Root { get; }

    public bool Exists(string relativePath)
    {
        try
        {
            return File.Exists(Resolve(relativePath));
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<WorkspaceFile> List(string? globPattern, int limit)
    {
        var matcher = BuildMatcher(globPattern);

        return EnumerateFiles()
            .Where(path => matcher(Relative(path)))
            .Take(limit)
            .Select(Describe)
            .ToList();
    }

    public async Task<string> ReadAsync(
        string relativePath, int? fromLine, int? toLine, CancellationToken ct = default)
    {
        var lines = (await ReadRawAsync(relativePath, ct)).Replace("\r\n", "\n").Split('\n');

        var from = Math.Max(fromLine ?? 1, 1);
        var to = Math.Min(toLine ?? lines.Length, lines.Length);

        if (from > lines.Length)
            return $"{relativePath} has {lines.Length} lines; line {from} is past the end.";

        var builder = new StringBuilder();
        builder.Append(relativePath).Append(" (lines ").Append(from).Append('-').Append(to)
            .Append(" of ").Append(lines.Length).AppendLine(")");

        // Line numbers, always. An agent that cannot cite a line cannot describe a change against
        // one, and the reviewer downstream needs the citation to check it.
        for (var index = from; index <= to; index++)
            builder.Append(index).Append('\t').AppendLine(lines[index - 1]);

        return builder.ToString();
    }

    public async Task<string> ReadRawAsync(string relativePath, CancellationToken ct = default)
    {
        var path = Resolve(relativePath);

        if (!File.Exists(path))
            throw new FileNotFoundException($"No file at '{relativePath}' under the repository root.");

        if (!_allowedExtensions.Contains(Path.GetExtension(path)))
        {
            throw new UnauthorizedAccessException(
                $"'{relativePath}' is not a readable source file. Readable extensions: {string.Join(", ", _allowedExtensions.Order())}.");
        }

        var info = new FileInfo(path);

        if (info.Length > _options.MaxFileBytes)
        {
            throw new InvalidOperationException(
                $"'{relativePath}' is {info.Length:N0} bytes, over the {_options.MaxFileBytes:N0} byte limit. " +
                "Read a line range instead, or search it.");
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task<IReadOnlyList<CodeMatch>> SearchAsync(
        string pattern,
        string? globPattern,
        bool isRegex,
        int limit,
        CancellationToken ct = default)
    {
        var matcher = BuildMatcher(globPattern);
        var matches = new List<CodeMatch>();

        Regex? regex = null;

        if (isRegex)
        {
            // A model-supplied regex can be catastrophically backtracking. The timeout bounds that;
            // an invalid pattern surfaces as a tool error the model can correct.
            regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        }

        foreach (var path in EnumerateFiles())
        {
            ct.ThrowIfCancellationRequested();

            if (matches.Count >= limit) break;

            var relative = Relative(path);
            if (!matcher(relative)) continue;
            if (!_allowedExtensions.Contains(Path.GetExtension(path))) continue;
            if (new FileInfo(path).Length > _options.MaxFileBytes) continue;

            var lines = await File.ReadAllLinesAsync(path, ct);

            for (var index = 0; index < lines.Length && matches.Count < limit; index++)
            {
                var line = lines[index];

                var hit = regex is null
                    ? line.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                    : regex.IsMatch(line);

                if (hit) matches.Add(new CodeMatch(relative, index + 1, line.Trim()));
            }
        }

        return matches;
    }

    private IEnumerable<string> EnumerateFiles()
        => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path));

    private bool IsExcluded(string path)
        => Relative(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipLast(1)
            .Any(_excluded.Contains);

    private WorkspaceFile Describe(string path)
    {
        var info = new FileInfo(path);

        // Line counts are only meaningful for text, and only affordable for small files — the point
        // is to help an agent judge whether a file is worth reading, not to be exact.
        var lines = _allowedExtensions.Contains(info.Extension) && info.Length <= _options.MaxFileBytes
            ? File.ReadLines(path).Count()
            : 0;

        return new WorkspaceFile(Relative(path), lines, info.Length, info.LastWriteTimeUtc);
    }

    private string Relative(string absolutePath)
        => Path.GetRelativePath(Root, absolutePath).Replace('\\', '/');

    /// <summary>
    /// Turns a relative path into an absolute one and proves it stays inside the root. The check is
    /// on the resolved full path, so <c>..</c> segments, symlink-shaped strings, and absolute paths
    /// are all caught by the same comparison.
    /// </summary>
    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("A file path is required.");

        var combined = Path.GetFullPath(Path.Combine(Root, relativePath.Replace('\\', '/')));

        if (!combined.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"'{relativePath}' is outside the repository root.");

        return combined;
    }

    /// <summary>
    /// Compiles a glob into a predicate. Hand-rolled rather than pulling in a globbing package for
    /// three wildcards: <c>**</c> crosses directories, <c>*</c> does not, <c>?</c> is one character.
    /// </summary>
    private static Func<string, bool> BuildMatcher(string? globPattern)
    {
        if (string.IsNullOrWhiteSpace(globPattern)) return _ => true;

        var normalized = globPattern.Replace('\\', '/').Trim();

        var expression = new StringBuilder("^");

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];

            if (character == '*')
            {
                if (index + 1 < normalized.Length && normalized[index + 1] == '*')
                {
                    expression.Append(".*");
                    index++;

                    // Swallow the slash after "**" so "src/**/*.cs" also matches "src/a.cs".
                    if (index + 1 < normalized.Length && normalized[index + 1] == '/') index++;
                }
                else
                {
                    expression.Append("[^/]*");
                }

                continue;
            }

            expression.Append(character == '?' ? "[^/]" : Regex.Escape(character.ToString()));
        }

        expression.Append('$');

        var regex = new Regex(expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return path => regex.IsMatch(path);
    }

    /// <summary>
    /// Walks up from the running binary looking for a solution file. Falls back to the current
    /// directory, which keeps a misconfigured deployment working on a smaller tree rather than
    /// failing at startup over a debugging aid.
    /// </summary>
    private static string ResolveRoot(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));

        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.slnx").Any() || directory.EnumerateFiles("*.sln").Any())
                return Path.TrimEndingDirectorySeparator(directory.FullName);

            directory = directory.Parent;
        }

        return Path.TrimEndingDirectorySeparator(Directory.GetCurrentDirectory());
    }
}

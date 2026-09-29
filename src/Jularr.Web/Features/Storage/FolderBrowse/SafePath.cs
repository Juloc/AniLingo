namespace Jularr.Web.Features.Storage.FolderBrowse;

/// <summary>
/// The path primitives every browse operation is built on: strict normalization, containment and a
/// managed realpath that follows symbolic links itself, so a link can never carry a request out of
/// the storage roots unnoticed.
/// </summary>
internal static class SafePath
{
    /// <summary>The kernel gives up after 40 links; so does this.</summary>
    private const int MaxLinkHops = 40;

    private static readonly char[] Separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
        ? [Path.DirectorySeparatorChar]
        : [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static StringComparer Comparer { get; } =
        Comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Validates an absolute path and returns its canonical lexical form: no <c>.</c> segments, no
    /// duplicate or trailing separators. A <c>..</c> segment is refused, not collapsed.
    /// </summary>
    public static PathProblem TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return PathProblem.Invalid;
        }

        if (input.Any(char.IsControl))
        {
            return PathProblem.Invalid;
        }

        var trimmed = input.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            return PathProblem.NotAbsolute;
        }

        if (Split(trimmed).Any(segment => segment == ".."))
        {
            return PathProblem.Traversal;
        }

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PathProblem.Invalid;
        }

        var root = Path.GetPathRoot(full) ?? string.Empty;
        normalized = full.Length > root.Length ? full.TrimEnd(Separators) : full;
        return PathProblem.None;
    }

    public static IEnumerable<string> Split(string path) =>
        path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when <paramref name="candidate"/> is <paramref name="root"/> or lies below it.</summary>
    public static bool IsWithin(string root, string candidate)
    {
        var trimmedRoot = root.Length > 1 ? root.TrimEnd(Separators) : root;
        if (candidate.Equals(trimmedRoot, Comparison))
        {
            return true;
        }

        var prefix = trimmedRoot.EndsWith(Path.DirectorySeparatorChar) || trimmedRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? trimmedRoot
            : trimmedRoot + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, Comparison);
    }

    /// <summary>The folder above a normalized path, or null at a file system root.</summary>
    public static string? Parent(string normalized)
    {
        var parent = Path.GetDirectoryName(normalized);
        return string.IsNullOrEmpty(parent) ? null : parent;
    }

    public static bool SamePath(string left, string right) => left.Equals(right, Comparison);

    /// <summary>
    /// Resolves every symbolic link on the way to <paramref name="normalized"/> (a normalized,
    /// absolute path) the way the kernel does, and returns the link-free path. Components that do
    /// not exist are kept as written: nothing can be linked below a missing folder. Returns null and
    /// sets <paramref name="problem"/> when a folder on the way is off limits or the links loop.
    /// </summary>
    public static string? ResolveReal(string normalized, out PathProblem problem)
    {
        problem = PathProblem.None;
        var root = Path.GetPathRoot(normalized) ?? string.Empty;
        var pending = new Stack<string>(Split(normalized[root.Length..]).Reverse());
        var current = root;
        var hops = 0;
        var missing = false;

        while (pending.Count > 0)
        {
            var segment = pending.Pop();
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                current = Parent(current) ?? current;
                continue;
            }

            var candidate = Path.Combine(current, segment);
            if (missing)
            {
                current = candidate;
                continue;
            }

            string? linkTarget;
            try
            {
                linkTarget = new DirectoryInfo(candidate).LinkTarget;
            }
            catch (UnauthorizedAccessException)
            {
                problem = PathProblem.NotReadable;
                return null;
            }
            catch (IOException)
            {
                problem = PathProblem.Unavailable;
                return null;
            }

            if (linkTarget is null)
            {
                missing = !Path.Exists(candidate);
                current = candidate;
                continue;
            }

            if (++hops > MaxLinkHops)
            {
                problem = PathProblem.Unavailable;
                return null;
            }

            // A relative target is relative to the folder that holds the link, which is `current`.
            if (Path.IsPathRooted(linkTarget) && Path.GetPathRoot(linkTarget) is { Length: > 0 } targetRoot)
            {
                current = targetRoot;
                linkTarget = linkTarget[targetRoot.Length..];
            }

            foreach (var part in Split(linkTarget).Reverse())
            {
                pending.Push(part);
            }
        }

        return current;
    }
}

namespace AniLingo.Web.Features.Acquisition.Naming;

// The filesystem operations the rename executor depends on for safety decisions. Methods are
// virtual so tests can simulate read-only mounts, other volumes and mid-rename failures.
public class AnimeRenameFileSystem
{
    private const string MountTable = "/proc/self/mounts";

    public virtual void MoveFile(string sourcePath, string targetPath) =>
        File.Move(sourcePath, targetPath, overwrite: false);

    public virtual void MoveDirectory(string sourcePath, string targetPath) =>
        Directory.Move(sourcePath, targetPath);

    // Null when AniLingo can create files in the directory; otherwise an operator-facing reason.
    // A read-only mount is detected from the mount table first so the common NAS case does not
    // need a write attempt; the probe file then catches permission-based read-only access.
    public virtual string? GetWriteBlocker(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var mount = FindMount(fullPath);
        if (mount is { ReadOnly: true })
        {
            return $"'{mount.Value.MountPoint}' is mounted read-only.";
        }

        var probePath = Path.Combine(fullPath, $".anilingo-write-probe-{Guid.NewGuid():N}");
        try
        {
            using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"'{fullPath}' is not writable by AniLingo ({exception.Message}).";
        }
    }

    // Identifies the filesystem a path lives on. A move between different keys would be a
    // copy+delete instead of an atomic rename, so the planner refuses it.
    public virtual string GetVolumeKey(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            return (Path.GetPathRoot(fullPath) ?? fullPath).ToUpperInvariant();
        }

        return FindMount(fullPath)?.MountPoint ?? "/";
    }

    private static (string MountPoint, bool ReadOnly)? FindMount(string fullPath)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(MountTable))
        {
            return null;
        }

        (string MountPoint, bool ReadOnly)? best = null;
        try
        {
            foreach (var line in File.ReadLines(MountTable))
            {
                var fields = line.Split(' ');
                if (fields.Length < 4)
                {
                    continue;
                }

                var mountPoint = UnescapeMountPath(fields[1]);
                var isParent = mountPoint == "/" ||
                               fullPath.Equals(mountPoint, StringComparison.Ordinal) ||
                               fullPath.StartsWith(mountPoint.TrimEnd('/') + "/", StringComparison.Ordinal);
                if (!isParent || (best is not null && best.Value.MountPoint.Length > mountPoint.Length))
                {
                    continue;
                }

                best = (mountPoint, fields[3].Split(',').Contains("ro", StringComparer.Ordinal));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return best;
    }

    // The kernel escapes space, tab, newline and backslash in mount paths as octal sequences.
    private static string UnescapeMountPath(string value) =>
        value
            .Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);
}

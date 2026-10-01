namespace Jularr.Web.Features.Admin;

/// <summary>
/// Maps a path to the filesystem visible inside the Jularr container. This intentionally reads the
/// current mount namespace only; it never inventories host disks or sibling containers.
/// </summary>
public static class ContainerMounts
{
    private const string MountInfoPath = "/proc/self/mountinfo";

    public static string? FileSystemTypeForPath(string path)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return Parse(File.ReadLines(MountInfoPath))
                .Where(mount => IsAtOrBelow(fullPath, mount.MountPoint))
                .OrderByDescending(mount => mount.MountPoint.Length)
                .Select(mount => mount.FileSystemType)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Parses Linux mountinfo rows without touching the filesystem.</summary>
    public static IReadOnlyList<ContainerMount> Parse(IEnumerable<string> lines)
    {
        var mounts = new List<ContainerMount>();
        foreach (var line in lines)
        {
            var separator = line.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var left = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var right = line[(separator + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (left.Length < 5 || right.Length < 1)
            {
                continue;
            }

            mounts.Add(new ContainerMount(Unescape(left[4]), right[0]));
        }

        return mounts;
    }

    private static bool IsAtOrBelow(string path, string mountPoint) =>
        mountPoint == "/" || path.Equals(mountPoint, StringComparison.Ordinal) ||
        (path.StartsWith(mountPoint, StringComparison.Ordinal) && path.Length > mountPoint.Length && path[mountPoint.Length] == '/');

    private static string Unescape(string value) => value
        .Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);
}

public sealed record ContainerMount(string MountPoint, string FileSystemType);

using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Storage.FolderBrowse;

/// <summary>
/// The mounts the running process sees. Reading them at request time keeps the browser aligned
/// with the container as it is now: nothing about an installation is hardcoded.
/// </summary>
public interface IMountTable
{
    IReadOnlyList<MountPoint> GetMounts();
}

/// <summary>
/// Linux: <c>/proc/self/mountinfo</c>, which carries the read-only flag of bind mounts. Windows and
/// macOS (development machines): the ready drives and volumes.
/// </summary>
public sealed class SystemMountTable(string mountInfoPath = SystemMountTable.DefaultMountInfoPath) : IMountTable
{
    public const string DefaultMountInfoPath = "/proc/self/mountinfo";

    public IReadOnlyList<MountPoint> GetMounts()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                return MountInfo.Parse(File.ReadLines(mountInfoPath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        try
        {
            return DriveInfo.GetDrives()
                .Where(IsUsableDrive)
                .Select(drive => new MountPoint(drive.Name, drive.DriveFormat, null, ReadOnly: false))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsUsableDrive(DriveInfo drive)
    {
        try
        {
            return drive.DriveType is DriveType.Fixed or DriveType.Network or DriveType.Removable
                && drive.IsReady;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Parser for the kernel's <c>mountinfo</c> format (proc(5)).</summary>
public static partial class MountInfo
{
    public static IReadOnlyList<MountPoint> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // Stacked mounts share a mount point: the last line is the one on top.
        var byPath = new Dictionary<string, MountPoint>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (ParseLine(line) is { } mount)
            {
                byPath[mount.Path] = mount;
            }
        }

        return byPath.Values.ToArray();
    }

    // 36 35 98:0 /mnt1 /mnt2 rw,noatime master:1 - ext3 /dev/root rw,errors=continue
    // id parent major:minor root mountpoint options [optional fields...] - fstype source superoptions
    public static MountPoint? ParseLine(string line)
    {
        var separator = line.IndexOf(" - ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        var head = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tail = line[(separator + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (head.Length < 6 || tail.Length < 2)
        {
            return null;
        }

        var options = head[5].Split(',');
        var superOptions = tail.Length > 2 ? tail[2].Split(',') : [];
        var readOnly = options.Contains("ro", StringComparer.Ordinal)
            || superOptions.Contains("ro", StringComparer.Ordinal);
        return new MountPoint(Unescape(head[4]), tail[0], Unescape(tail[1]), readOnly);
    }

    // The kernel writes space, tab, newline and backslash as \ooo octal escapes.
    private static string Unescape(string value) =>
        value.Contains('\\', StringComparison.Ordinal)
            ? OctalEscape().Replace(value, match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString())
            : value;

    [GeneratedRegex(@"\\([0-7]{3})")]
    private static partial Regex OctalEscape();
}

/// <summary>
/// Separates the mounts an admin attached to the container (a media volume, the data volume) from
/// the ones the runtime and image bring along: pseudo file systems, the image layers, the
/// container's own system folders and the root itself.
/// </summary>
public static class StorageMountFilter
{
    private static readonly HashSet<string> NonStorageFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "sysfs", "devpts", "devtmpfs", "devfs", "tmpfs", "ramfs", "cgroup", "cgroup2", "mqueue",
        "overlay", "aufs", "squashfs", "securityfs", "debugfs", "tracefs", "configfs", "fusectl",
        "binfmt_misc", "autofs", "bpf", "pstore", "hugetlbfs", "rpc_pipefs", "nsfs", "selinuxfs",
        "efivarfs", "fuse.lxcfs", "fuse.gvfsd-fuse"
    };

    private static readonly string[] SystemFolders =
    [
        "/proc", "/sys", "/dev", "/run", "/etc", "/usr", "/bin", "/sbin", "/lib", "/lib32", "/lib64", "/boot"
    ];

    public static bool IsStorage(MountPoint mount)
    {
        if (string.IsNullOrWhiteSpace(mount.Path) || !Path.IsPathFullyQualified(mount.Path))
        {
            return false;
        }

        if (NonStorageFileSystems.Contains(mount.FileSystem))
        {
            return false;
        }

        if (mount.Path == "/")
        {
            return false;
        }

        return !SystemFolders.Any(folder => SafePath.IsWithin(folder, mount.Path));
    }
}

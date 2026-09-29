using System.Runtime.InteropServices;

namespace Jularr.Web.Features.Storage.FolderBrowse;

/// <summary>What the runtime user is allowed to do in a folder.</summary>
public interface IDirectoryAccess
{
    bool CanRead(string directory);

    bool CanWrite(string directory);
}

/// <summary>
/// On Linux and macOS this is <c>access(2)</c>: it answers for the user Jularr runs as, writes
/// nothing and works the same on local disks, NFS and SMB mounts. Windows (development machines
/// only) has no such call, so it lists the folder and, for writing, creates and removes a temporary
/// file.
/// </summary>
public sealed class DirectoryAccess : IDirectoryAccess
{
    private const int ReadAccess = 4;
    private const int WriteAccess = 2;
    private const int SearchAccess = 1;

    public bool CanRead(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Unix.access(directory, ReadAccess | SearchAccess) == 0;
        }

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            entries.MoveNext();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool CanWrite(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Unix.access(directory, WriteAccess | SearchAccess) == 0;
        }

        try
        {
            var probe = Path.Combine(directory, $".jularr-write-probe-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static class Unix
    {
        [DllImport("libc", SetLastError = true, EntryPoint = "access")]
        public static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
    }
}

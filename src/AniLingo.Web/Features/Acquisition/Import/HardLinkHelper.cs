using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AniLingo.Web.Features.Acquisition.Import;

/// <summary>Thrown when a hardlink cannot be created because source and target are on different filesystems.</summary>
public sealed class CrossDeviceLinkException(string path)
    : IOException($"'{path}' is on a different filesystem than the destination folder; a hardlink is not possible there.");

/// <summary>Creates a hardlink for a completed-download import. Injectable so tests can fake a cross-device failure.</summary>
public interface IHardLinkCreator
{
    /// <summary>
    /// Creates a hardlink (a second directory entry for the same file data, no extra disk space,
    /// both names independent afterwards) at <paramref name="destinationPath"/> for the file at
    /// <paramref name="sourcePath"/>. Throws <see cref="CrossDeviceLinkException"/> when source and
    /// destination are on different filesystems.
    /// </summary>
    void CreateHardLink(string sourcePath, string destinationPath);
}

/// <summary>Real filesystem hardlink creation via the platform's native API.</summary>
public sealed class FileSystemHardLinkCreator : IHardLinkCreator
{
    public void CreateHardLink(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (OperatingSystem.IsWindows())
        {
            CreateWindowsHardLink(sourcePath, destinationPath);
        }
        else
        {
            CreateUnixHardLink(sourcePath, destinationPath);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateWindowsHardLink(string sourcePath, string destinationPath)
    {
        if (Windows.CreateHardLinkW(destinationPath, sourcePath, IntPtr.Zero))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        // ERROR_NOT_SAME_DEVICE
        if (error == 17)
        {
            throw new CrossDeviceLinkException(sourcePath);
        }

        throw new IOException(
            $"Could not create a hardlink for '{sourcePath}': {new Win32Exception(error).Message}");
    }

    private static void CreateUnixHardLink(string sourcePath, string destinationPath)
    {
        if (Unix.link(sourcePath, destinationPath) == 0)
        {
            return;
        }

        var errno = Marshal.GetLastPInvokeError();
        // EXDEV: cross-device link.
        if (errno == 18)
        {
            throw new CrossDeviceLinkException(sourcePath);
        }

        throw new IOException(
            $"Could not create a hardlink for '{sourcePath}': {new Win32Exception(errno).Message}");
    }

    [SupportedOSPlatform("windows")]
    private static class Windows
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateHardLinkW(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);
    }

    private static class Unix
    {
        [DllImport("libc", SetLastError = true, EntryPoint = "link")]
        public static extern int link(string oldpath, string newpath);
    }
}

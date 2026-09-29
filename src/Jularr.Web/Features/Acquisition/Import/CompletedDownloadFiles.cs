namespace Jularr.Web.Features.Acquisition.Import;

/// <summary>One file of a completed download.</summary>
public sealed record CompletedDownloadFile(
    string Path,
    long SizeBytes);

/// <summary>
/// Finds and classifies the files of a completed download, for every importer that places video
/// files (Anime today, Movies and TV later): what is in the download folder, which files are the
/// video and which are its subtitle/info sidecars, and which are only download leftovers.
/// Media-specific matching (which episode a video is) stays with the importer.
/// </summary>
public static class CompletedDownloadFiles
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".m4v", ".avi", ".ts", ".m2ts", ".webm"
        };

    private static readonly HashSet<string> SidecarExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".srt", ".ass", ".ssa", ".vtt", ".nfo"
        };

    private static readonly HashSet<string> LeftoverExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".jpg", ".png", ".nzb", ".par2", ".sfv"
        };

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>A subtitle or info file that belongs to a video with the same name.</summary>
    public static bool IsSidecar(string path) =>
        SidecarExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// A sidecar or a download leftover (posters, par2/sfv/nzb files, text notes): a file nobody
    /// needs to hear about when it is not imported.
    /// </summary>
    public static bool IsSidecarOrJunk(string path) =>
        IsSidecar(path) || LeftoverExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Every file of a completed download, in a stable order: the file itself when the path is a
    /// file, otherwise all files below the folder. <paramref name="error"/> says why nothing could
    /// be listed (the path does not exist, or is not mounted or readable in Jularr).
    /// </summary>
    public static IReadOnlyList<CompletedDownloadFile> Enumerate(string downloadPath, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(downloadPath))
            {
                return [new CompletedDownloadFile(Path.GetFullPath(downloadPath), new FileInfo(downloadPath).Length)];
            }

            if (!Directory.Exists(downloadPath))
            {
                error = $"The completed download path does not exist or is not mounted in Jularr: {downloadPath}";
                return [];
            }

            return Directory
                .EnumerateFiles(downloadPath, "*", SearchOption.AllDirectories)
                .Select(path => new CompletedDownloadFile(Path.GetFullPath(path), new FileInfo(path).Length))
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"The completed download path could not be read: {exception.Message}";
            return [];
        }
    }
}

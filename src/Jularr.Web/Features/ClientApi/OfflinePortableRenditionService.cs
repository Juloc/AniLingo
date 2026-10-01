using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jularr.Web.Infrastructure;
using Jularr.Web.Features.Library;

namespace Jularr.Web.Features.ClientApi;

/// <summary>
/// Creates a disposable PWA download rendition only when the original container is not portable
/// across Safari and Chromium. Originals are never changed. A later manifest/content lookup uses
/// the same deterministic cache key, and stale copies are removed opportunistically after the
/// short resume grace period.
/// </summary>
public sealed class OfflinePortableRenditionService(MediaProcessRunner processes, IMediaProbeRunner probeRunner)
{
    private const string Root = "/data/offline-pwa-renditions";
    private static readonly TimeSpan ResumeGrace = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Timeout = TimeSpan.FromHours(6);
    private static readonly ConcurrentDictionary<string, Task<string?>> InFlight = new(StringComparer.Ordinal);

    public async Task<OfflinePortableRendition?> GetAsync(
        string sourcePath,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourcePath);
        if (await IsPortableOriginalAsync(sourcePath, sourceType, extension, cancellationToken))
        {
            return new(sourcePath, ContentType(sourcePath), IsTemporary: false);
        }

        var output = BuildPath(sourcePath, sourceType);
        if (!File.Exists(output))
        {
            var task = InFlight.GetOrAdd(output, _ => CreateAsync(sourcePath, sourceType, output));
            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    await task.WaitAsync(cancellationToken);
                }
                else
                {
                    await task;
                }
            }
            finally
            {
                if (task.IsCompleted) InFlight.TryRemove(output, out _);
            }
        }

        if (!File.Exists(output) || new FileInfo(output).Length == 0) return null;
        PruneExpired();
        return new(output, sourceType switch
        {
            "audio" => "audio/mp4",
            "subtitle" => "text/vtt",
            _ => "video/mp4"
        }, IsTemporary: true);
    }

    public static IReadOnlyList<string> BuildArguments(string sourcePath, string sourceType, string outputPath) =>
        sourceType == "subtitle"
            ? ["-v", "error", "-nostdin", "-y", "-i", sourcePath, "-map", "0:s:0?", "-c:s", "webvtt", outputPath]
            : sourceType == "audio"
            ? ["-v", "error", "-nostdin", "-y", "-i", sourcePath, "-map", "0:a:0", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4", outputPath]
            : ["-v", "error", "-nostdin", "-y", "-i", sourcePath, "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn", "-vf", "scale=1280:720:force_original_aspect_ratio=decrease", "-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4", outputPath];

    private async Task<string?> CreateAsync(string sourcePath, string sourceType, string outputPath)
    {
        try
        {
            Directory.CreateDirectory(Root);
            PruneExpired();
            var temporary = outputPath + ".tmp";
            TryDelete(temporary);
            var result = await processes.RunAsync("ffmpeg", BuildArguments(sourcePath, sourceType, temporary), Timeout, CancellationToken.None);
            if (result is null || result.ExitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length == 0)
            {
                TryDelete(temporary);
                return null;
            }
            File.Move(temporary, outputPath, overwrite: true);
            return outputPath;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private async Task<bool> IsPortableOriginalAsync(
        string sourcePath,
        string sourceType,
        string extension,
        CancellationToken cancellationToken)
    {
        if (sourceType == "subtitle")
        {
            return extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase);
        }

        if (sourceType == "audio")
        {
            return extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4b", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
        }

        if (!extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var probe = await probeRunner.ProbeAsync(sourcePath, cancellationToken);
        if (probe.Status != MediaProbeRunStatus.Completed)
        {
            // Conservative failure: an unknown source gets a portable rendition rather than a file
            // that happens to play only on the server's browser.
            return false;
        }

        try
        {
            var technical = MediaProbeParser.Parse(probe.Output);
            var video = technical.Video;
            var audio = technical.AudioStreams.FirstOrDefault();
            return string.Equals(video?.Codec, "h264", StringComparison.OrdinalIgnoreCase)
                && video?.PixelFormat is "yuv420p" or "yuvj420p"
                && (audio is null || audio.Codec is "aac" or "mp3");
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static string BuildPath(string sourcePath, string sourceType)
    {
        var identity = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(sourcePath)));
        var suffix = sourceType switch
        {
            "audio" => ".m4a",
            "subtitle" => ".vtt",
            _ => ".mp4"
        };
        return Path.Combine(Root, Convert.ToHexString(identity)[..24].ToLowerInvariant() + suffix);
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".vtt" => "text/vtt",
        ".m4a" or ".m4b" => "audio/mp4",
        ".mp3" => "audio/mpeg",
        _ => "video/mp4"
    };

    private static void PruneExpired()
    {
        if (!Directory.Exists(Root)) return;
        var cutoff = DateTime.UtcNow - ResumeGrace;
        foreach (var path in Directory.EnumerateFiles(Root))
        {
            try { if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed record OfflinePortableRendition(string Path, string ContentType, bool IsTemporary);

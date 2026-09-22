using System.Collections.Concurrent;
using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Playback;

public enum PlaybackPreparationStatus
{
    None,
    Queued,
    Processing,
    Ready,
    Failed
}

public enum PlaybackPreparationKind
{
    CompatibleRemux,
    DeviceHevcRemux,
    ServerH264Transcode
}

public enum PlaybackRequestedMode
{
    Device,
    Server
}

public sealed record PlaybackPreparationState(
    PlaybackPreparationStatus Status,
    string? Message = null);

public sealed class PlaybackPreparationTracker
{
    private readonly ConcurrentDictionary<(Guid MediaFileId, PlaybackPreparationKind Kind), PlaybackPreparationState>
        states = new();

    public PlaybackPreparationState Get(Guid mediaFileId, PlaybackPreparationKind kind) =>
        states.TryGetValue((mediaFileId, kind), out var state)
            ? state
            : new PlaybackPreparationState(PlaybackPreparationStatus.None);

    public bool TryQueue(Guid mediaFileId, PlaybackPreparationKind kind)
    {
        var key = (mediaFileId, kind);

        while (true)
        {
            var current = Get(mediaFileId, kind);
            if (current.Status is PlaybackPreparationStatus.Queued or PlaybackPreparationStatus.Processing)
            {
                return false;
            }

            var queued = new PlaybackPreparationState(PlaybackPreparationStatus.Queued);

            if (current.Status == PlaybackPreparationStatus.None)
            {
                if (states.TryAdd(key, queued))
                {
                    return true;
                }

                continue;
            }

            if (states.TryUpdate(key, queued, current))
            {
                return true;
            }
        }
    }

    public void MarkProcessing(Guid mediaFileId, PlaybackPreparationKind kind) =>
        states[(mediaFileId, kind)] = new PlaybackPreparationState(PlaybackPreparationStatus.Processing);

    public void MarkReady(Guid mediaFileId, PlaybackPreparationKind kind) =>
        states[(mediaFileId, kind)] = new PlaybackPreparationState(PlaybackPreparationStatus.Ready);

    public void MarkFailed(Guid mediaFileId, PlaybackPreparationKind kind, string message) =>
        states[(mediaFileId, kind)] = new PlaybackPreparationState(
            PlaybackPreparationStatus.Failed,
            message);
}

public sealed record PlaybackProbeResult(
    string? VideoCodec,
    string? PixelFormat,
    string? AudioCodec);

public enum PlaybackAudioMode
{
    None,
    Copy,
    Aac
}

public enum PlaybackVideoMode
{
    Copy,
    H264
}

public sealed record PlaybackPreparationPlan(
    bool CanPrepare,
    PlaybackPreparationKind? Kind,
    PlaybackVideoMode VideoMode,
    PlaybackAudioMode AudioMode,
    bool TagHevcAsHvc1,
    string Message)
{
    private static readonly HashSet<string> BrowserH264PixelFormats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "yuv420p",
            "yuvj420p"
        };

    public static PlaybackPreparationPlan Build(
        PlaybackProbeResult probe,
        PlaybackRequestedMode requestedMode)
    {
        var audioMode = BuildAudioMode(probe.AudioCodec);

        if (string.Equals(probe.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase) &&
            probe.PixelFormat is not null &&
            BrowserH264PixelFormats.Contains(probe.PixelFormat))
        {
            return new PlaybackPreparationPlan(
                true,
                PlaybackPreparationKind.CompatibleRemux,
                PlaybackVideoMode.Copy,
                audioMode,
                false,
                audioMode == PlaybackAudioMode.Aac
                    ? $"H.264 video will be copied; {probe.AudioCodec} audio will be converted to AAC."
                    : "H.264 video and compatible audio will be copied without video re-encoding.");
        }

        if (requestedMode == PlaybackRequestedMode.Device)
        {
            if (string.Equals(probe.VideoCodec, "hevc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(probe.VideoCodec, "h265", StringComparison.OrdinalIgnoreCase))
            {
                return new PlaybackPreparationPlan(
                    true,
                    PlaybackPreparationKind.DeviceHevcRemux,
                    PlaybackVideoMode.Copy,
                    audioMode,
                    true,
                    audioMode == PlaybackAudioMode.Aac
                        ? $"HEVC video will stay untouched; {probe.AudioCodec} audio will be converted to AAC."
                        : "HEVC video will stay untouched and be remuxed for device decoding.");
            }

            return Unsupported(
                $"Video codec {probe.VideoCodec ?? "unknown"} cannot use the device-only preparation path.");
        }

        if (string.IsNullOrWhiteSpace(probe.VideoCodec))
        {
            return Unsupported("No video stream was detected.");
        }

        return new PlaybackPreparationPlan(
            true,
            PlaybackPreparationKind.ServerH264Transcode,
            PlaybackVideoMode.H264,
            audioMode,
            false,
            $"Video codec {probe.VideoCodec} will be transcoded to H.264 on the server.");
    }

    private static PlaybackAudioMode BuildAudioMode(string? audioCodec) =>
        string.IsNullOrWhiteSpace(audioCodec)
            ? PlaybackAudioMode.None
            : string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase)
                ? PlaybackAudioMode.Copy
                : PlaybackAudioMode.Aac;

    private static PlaybackPreparationPlan Unsupported(string message) =>
        new(
            false,
            null,
            PlaybackVideoMode.Copy,
            PlaybackAudioMode.None,
            false,
            message);
}

public static class PlaybackCache
{
    public const string RootPath = "/data/playback-cache";

    public static string BuildPath(
        Guid mediaFileId,
        long sizeBytes,
        DateTime sourceUpdatedAt,
        PlaybackPreparationKind kind) =>
        Path.Combine(
            RootPath,
            $"{mediaFileId:N}-{sizeBytes}-{sourceUpdatedAt.Ticks}-{Suffix(kind)}.mp4");

    public static string BuildPattern(Guid mediaFileId, PlaybackPreparationKind kind) =>
        $"{mediaFileId:N}-*-{Suffix(kind)}.mp4";

    private static string Suffix(PlaybackPreparationKind kind) =>
        kind switch
        {
            PlaybackPreparationKind.CompatibleRemux => "compatible",
            PlaybackPreparationKind.DeviceHevcRemux => "device-hevc",
            PlaybackPreparationKind.ServerH264Transcode => "server-h264",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}

public sealed class PlaybackMediaProbe(
    MediaProcessRunner processRunner,
    ILogger<PlaybackMediaProbe> logger)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, ProbeCacheEntry> cache =
        new(StringComparer.Ordinal);

    public async Task<PlaybackProbeResult?> ProbeAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(mediaPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            return null;
        }

        if (cache.TryGetValue(fullPath, out var cached) &&
            cached.SizeBytes == info.Length &&
            cached.LastWriteTimeUtc == info.LastWriteTimeUtc)
        {
            return cached.Result;
        }

        var result = await processRunner.RunAsync(
            "ffprobe",
            [
                "-v", "error",
                "-show_entries", "stream=codec_type,codec_name,pix_fmt",
                "-of", "json",
                fullPath
            ],
            ProbeTimeout,
            cancellationToken);

        if (result is null || result.ExitCode != 0)
        {
            if (result is not null)
            {
                logger.LogWarning(
                    "ffprobe failed while inspecting playback media {MediaPath}: {Error}",
                    mediaPath,
                    result.ErrorSummary);
            }

            return null;
        }

        try
        {
            var parsed = Parse(result.Output);
            cache[fullPath] = new ProbeCacheEntry(
                info.Length,
                info.LastWriteTimeUtc,
                parsed);
            return parsed;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "ffprobe returned invalid JSON for playback media {MediaPath}.",
                mediaPath);
            return null;
        }
    }

    public static PlaybackProbeResult Parse(string probeJson)
    {
        using var document = JsonDocument.Parse(probeJson);
        string? videoCodec = null;
        string? pixelFormat = null;
        string? audioCodec = null;

        if (!document.RootElement.TryGetProperty("streams", out var streams) ||
            streams.ValueKind != JsonValueKind.Array)
        {
            return new PlaybackProbeResult(null, null, null);
        }

        foreach (var stream in streams.EnumerateArray())
        {
            var type = ReadString(stream, "codec_type");
            if (videoCodec is null && string.Equals(type, "video", StringComparison.OrdinalIgnoreCase))
            {
                videoCodec = ReadString(stream, "codec_name");
                pixelFormat = ReadString(stream, "pix_fmt");
            }
            else if (audioCodec is null && string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase))
            {
                audioCodec = ReadString(stream, "codec_name");
            }
        }

        return new PlaybackProbeResult(videoCodec, pixelFormat, audioCodec);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record ProbeCacheEntry(
        long SizeBytes,
        DateTime LastWriteTimeUtc,
        PlaybackProbeResult Result);
}

public sealed class PlaybackPreparationService(
    AppDbContext db,
    PlaybackMediaProbe probe,
    PlaybackPreparationTracker tracker,
    MediaProcessRunner processRunner,
    ILogger<PlaybackPreparationService> logger)
{
    private static readonly TimeSpan PreparationTimeout = TimeSpan.FromHours(6);

    public async Task PrepareAsync(
        Guid episodeId,
        PlaybackRequestedMode requestedMode,
        CancellationToken cancellationToken)
    {
        var media = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId)
            .OrderBy(x => x.Path)
            .Select(x => new
            {
                x.Id,
                x.Path,
                x.SizeBytes,
                x.LastWriteTimeUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (media is null)
        {
            return;
        }

        var mediaProbe = await probe.ProbeAsync(media.Path, cancellationToken);
        if (mediaProbe is null)
        {
            return;
        }

        var plan = PlaybackPreparationPlan.Build(mediaProbe, requestedMode);
        if (!plan.CanPrepare || plan.Kind is null)
        {
            return;
        }

        var kind = plan.Kind.Value;
        tracker.MarkProcessing(media.Id, kind);

        var outputPath = PlaybackCache.BuildPath(
            media.Id,
            media.SizeBytes,
            media.LastWriteTimeUtc,
            kind);

        if (File.Exists(outputPath))
        {
            tracker.MarkReady(media.Id, kind);
            return;
        }

        Directory.CreateDirectory(PlaybackCache.RootPath);

        foreach (var stalePath in Directory.EnumerateFiles(
                     PlaybackCache.RootPath,
                     PlaybackCache.BuildPattern(media.Id, kind)))
        {
            if (!string.Equals(stalePath, outputPath, StringComparison.Ordinal))
            {
                File.Delete(stalePath);
            }
        }

        var temporaryPath = outputPath + ".tmp";
        TryDelete(temporaryPath);

        var arguments = new List<string>
        {
            "-v", "error",
            "-nostdin",
            "-y",
            "-i", Path.GetFullPath(media.Path),
            "-map", "0:v:0",
            "-sn",
            "-dn"
        };

        if (plan.VideoMode == PlaybackVideoMode.Copy)
        {
            arguments.AddRange(["-c:v", "copy"]);
            if (plan.TagHevcAsHvc1)
            {
                arguments.AddRange(["-tag:v", "hvc1"]);
            }
        }
        else
        {
            arguments.AddRange([
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-crf", "22",
                "-pix_fmt", "yuv420p"
            ]);
        }

        switch (plan.AudioMode)
        {
            case PlaybackAudioMode.Copy:
                arguments.AddRange(["-map", "0:a:0?", "-c:a", "copy"]);
                break;
            case PlaybackAudioMode.Aac:
                arguments.AddRange(["-map", "0:a:0?", "-c:a", "aac", "-b:a", "192k"]);
                break;
        }

        arguments.AddRange(["-movflags", "+faststart", "-f", "mp4", temporaryPath]);

        try
        {
            var result = await processRunner.RunAsync(
                "ffmpeg",
                arguments,
                PreparationTimeout,
                cancellationToken);

            if (result is null ||
                result.ExitCode != 0 ||
                !File.Exists(temporaryPath) ||
                new FileInfo(temporaryPath).Length == 0)
            {
                TryDelete(temporaryPath);
                var message = result?.ErrorSummary;
                tracker.MarkFailed(
                    media.Id,
                    kind,
                    string.IsNullOrWhiteSpace(message)
                        ? "Playback preparation failed."
                        : $"Playback preparation failed: {message}");
                return;
            }

            File.Move(temporaryPath, outputPath);
            tracker.MarkReady(media.Id, kind);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            tracker.MarkFailed(media.Id, kind, "Could not write the playback cache.");
            logger.LogError(
                exception,
                "Could not prepare playback cache for {MediaPath}.",
                media.Path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}

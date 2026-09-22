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

public sealed record PlaybackPreparationState(
    PlaybackPreparationStatus Status,
    string? Message = null);

public sealed class PlaybackPreparationTracker
{
    private readonly ConcurrentDictionary<Guid, PlaybackPreparationState> states = new();

    public PlaybackPreparationState Get(Guid mediaFileId) =>
        states.TryGetValue(mediaFileId, out var state)
            ? state
            : new PlaybackPreparationState(PlaybackPreparationStatus.None);

    public bool TryQueue(Guid mediaFileId)
    {
        while (true)
        {
            var current = Get(mediaFileId);
            if (current.Status is PlaybackPreparationStatus.Queued or PlaybackPreparationStatus.Processing)
            {
                return false;
            }

            var queued = new PlaybackPreparationState(PlaybackPreparationStatus.Queued);

            if (current.Status == PlaybackPreparationStatus.None)
            {
                if (states.TryAdd(mediaFileId, queued))
                {
                    return true;
                }

                continue;
            }

            if (states.TryUpdate(mediaFileId, queued, current))
            {
                return true;
            }
        }
    }

    public void MarkProcessing(Guid mediaFileId) =>
        states[mediaFileId] = new PlaybackPreparationState(PlaybackPreparationStatus.Processing);

    public void MarkReady(Guid mediaFileId) =>
        states[mediaFileId] = new PlaybackPreparationState(PlaybackPreparationStatus.Ready);

    public void MarkFailed(Guid mediaFileId, string message) =>
        states[mediaFileId] = new PlaybackPreparationState(
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

public sealed record PlaybackRemuxPlan(
    bool CanPrepare,
    PlaybackAudioMode AudioMode,
    string Message)
{
    private static readonly HashSet<string> BrowserH264PixelFormats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "yuv420p",
            "yuvj420p"
        };

    public static PlaybackRemuxPlan Build(PlaybackProbeResult probe)
    {
        if (!string.Equals(probe.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            return new PlaybackRemuxPlan(
                false,
                PlaybackAudioMode.None,
                $"Video codec {probe.VideoCodec ?? "unknown"} needs video transcoding.");
        }

        if (probe.PixelFormat is null || !BrowserH264PixelFormats.Contains(probe.PixelFormat))
        {
            return new PlaybackRemuxPlan(
                false,
                PlaybackAudioMode.None,
                $"H.264 pixel format {probe.PixelFormat ?? "unknown"} is not safe for browser remux.");
        }

        var audioMode = string.IsNullOrWhiteSpace(probe.AudioCodec)
            ? PlaybackAudioMode.None
            : string.Equals(probe.AudioCodec, "aac", StringComparison.OrdinalIgnoreCase)
                ? PlaybackAudioMode.Copy
                : PlaybackAudioMode.Aac;

        return new PlaybackRemuxPlan(
            true,
            audioMode,
            audioMode == PlaybackAudioMode.Aac
                ? $"Video will be copied; {probe.AudioCodec} audio will be converted to AAC."
                : "Video and compatible audio will be copied without re-encoding.");
    }
}

public static class PlaybackCache
{
    public const string RootPath = "/data/playback-cache";

    public static string BuildPath(
        Guid mediaFileId,
        long sizeBytes,
        DateTimeOffset sourceUpdatedAt) =>
        Path.Combine(
            RootPath,
            $"{mediaFileId:N}-{sizeBytes}-{sourceUpdatedAt.UtcDateTime.Ticks}.mp4");

    public static string BuildPattern(Guid mediaFileId) =>
        $"{mediaFileId:N}-*.mp4";
}

public sealed class PlaybackMediaProbe(
    MediaProcessRunner processRunner,
    ILogger<PlaybackMediaProbe> logger)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    public async Task<PlaybackProbeResult?> ProbeAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "ffprobe",
            [
                "-v", "error",
                "-show_entries", "stream=codec_type,codec_name,pix_fmt",
                "-of", "json",
                Path.GetFullPath(mediaPath)
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
            return Parse(result.Output);
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
}

public sealed class PlaybackRemuxService(
    AppDbContext db,
    PlaybackMediaProbe probe,
    PlaybackPreparationTracker tracker,
    MediaProcessRunner processRunner,
    ILogger<PlaybackRemuxService> logger)
{
    private static readonly TimeSpan RemuxTimeout = TimeSpan.FromHours(1);

    public async Task PrepareAsync(Guid episodeId, CancellationToken cancellationToken)
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

        tracker.MarkProcessing(media.Id);

        var outputPath = PlaybackCache.BuildPath(
            media.Id,
            media.SizeBytes,
            media.LastWriteTimeUtc);

        if (File.Exists(outputPath))
        {
            tracker.MarkReady(media.Id);
            return;
        }

        var mediaProbe = await probe.ProbeAsync(media.Path, cancellationToken);
        if (mediaProbe is null)
        {
            tracker.MarkFailed(media.Id, "Media inspection failed.");
            return;
        }

        var plan = PlaybackRemuxPlan.Build(mediaProbe);
        if (!plan.CanPrepare)
        {
            tracker.MarkFailed(media.Id, plan.Message);
            return;
        }

        Directory.CreateDirectory(PlaybackCache.RootPath);

        foreach (var stalePath in Directory.EnumerateFiles(
                     PlaybackCache.RootPath,
                     PlaybackCache.BuildPattern(media.Id)))
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
            "-dn",
            "-c:v", "copy"
        };

        switch (plan.AudioMode)
        {
            case PlaybackAudioMode.Copy:
                arguments.AddRange(["-map", "0:a:0?", "-c:a", "copy"]);
                break;
            case PlaybackAudioMode.Aac:
                arguments.AddRange(["-map", "0:a:0?", "-c:a", "aac", "-b:a", "192k"]);
                break;
        }

        arguments.AddRange(["-movflags", "+faststart", temporaryPath]);

        try
        {
            var result = await processRunner.RunAsync(
                "ffmpeg",
                arguments,
                RemuxTimeout,
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
                    string.IsNullOrWhiteSpace(message)
                        ? "Browser playback preparation failed."
                        : $"Browser playback preparation failed: {message}");
                return;
            }

            File.Move(temporaryPath, outputPath);
            tracker.MarkReady(media.Id);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            tracker.MarkFailed(media.Id, "Could not write the playback cache.");
            logger.LogError(
                exception,
                "Could not prepare browser playback cache for {MediaPath}.",
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

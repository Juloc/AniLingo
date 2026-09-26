using System.Collections.Concurrent;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
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

public enum PlaybackTrackKind
{
    Audio,
    Subtitle
}

public sealed record PlaybackMediaTrack(
    int StreamIndex,
    PlaybackTrackKind Kind,
    string? Codec,
    string? Language,
    string? Title,
    bool IsDefault,
    bool IsForced,
    bool IsText);

// Playback's decision view of the canonical media inventory (MediaInventoryService).
public sealed record PlaybackProbeResult(
    string? VideoCodec,
    string? PixelFormat,
    string? AudioCodec,
    double? DurationSeconds = null,
    IReadOnlyList<PlaybackMediaTrack>? Tracks = null)
{
    public static PlaybackProbeResult From(MediaTechnicalInfo technical) =>
        new(
            technical.Video?.Codec,
            technical.Video?.PixelFormat,
            technical.AudioStreams.FirstOrDefault()?.Codec,
            technical.DurationSeconds,
            [
                .. technical.Streams.Select(stream => new PlaybackMediaTrack(
                    stream.Index,
                    stream.Kind == MediaStreamKind.Audio
                        ? PlaybackTrackKind.Audio
                        : PlaybackTrackKind.Subtitle,
                    stream.Codec,
                    stream.Language,
                    stream.Title,
                    stream.IsDefault,
                    stream.IsForced,
                    stream.IsText))
            ]);
}

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

public sealed class PlaybackPreparationService(
    AppDbContext db,
    MediaInventoryService mediaInventory,
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

        var inventory = await mediaInventory.EnsureAnalyzedAsync(media.Id, cancellationToken);
        if (inventory?.Technical is not { } technical)
        {
            return;
        }

        var plan = PlaybackPreparationPlan.Build(PlaybackProbeResult.From(technical), requestedMode);
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

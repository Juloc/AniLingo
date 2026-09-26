using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Playback;

public sealed record HlsPlaybackSession(
    Guid SessionId,
    Guid EpisodeId,
    string ProfileId,
    double StartSeconds,
    DateTimeOffset CreatedAtUtc,
    int? AudioStreamIndex = null,
    PlaybackQualityCap QualityCap = PlaybackQualityCap.Auto);

public sealed record HlsPlaybackAsset(
    string Path,
    string ContentType,
    bool EnableRangeProcessing);

public sealed class HlsPlaybackSessionManager : IDisposable
{
    public static HlsPlaybackSessionManager Shared { get; } = new();

    private HlsPlaybackSessionManager()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
    }
    public const int SegmentSeconds = 4;
    public const int PlaylistSegments = 12;
    public const int MaxSessions = 8;
    public const int MaxSessionsPerProfile = 2;
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(10);
    public const string RootPath = "/data/playback-cache/hls";

    private static readonly Regex SegmentPattern =
        new("^segment-[0-9]{5}\\.m4s$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object gate = new();
    private readonly ConcurrentDictionary<Guid, Entry> sessions = new();
    private bool disposed;

    public async Task<HlsPlaybackSession> StartAsync(
        Guid episodeId,
        string profileId,
        string sourcePath,
        double startSeconds,
        CancellationToken cancellationToken,
        int? audioStreamIndex = null,
        PlaybackQualityCap qualityCap = PlaybackQualityCap.Auto)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID is required.", nameof(profileId));
        }

        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds));
        }

        ThrowIfDisposed();
        CleanupExpired();

        Entry entry;
        lock (gate)
        {
            EvictForProfileCapacity(profileId);

            if (sessions.Count >= MaxSessions)
            {
                throw new InvalidOperationException(
                    "HLS fallback capacity is currently full.");
            }

            Directory.CreateDirectory(RootPath);
            var sessionId = Guid.NewGuid();
            var directory = Path.Combine(RootPath, sessionId.ToString("N"));
            Directory.CreateDirectory(directory);

            var process = StartProcess(
                sourcePath,
                directory,
                startSeconds,
                audioStreamIndex,
                qualityCap);

            entry = new Entry(
                sessionId,
                episodeId,
                profileId,
                directory,
                process,
                startSeconds,
                DateTimeOffset.UtcNow);
            sessions[sessionId] = entry;
        }

        try
        {
            await WaitForPlaylistAsync(entry, cancellationToken);
        }
        catch
        {
            Remove(entry.SessionId);
            throw;
        }

        return new HlsPlaybackSession(
            entry.SessionId,
            entry.EpisodeId,
            entry.ProfileId,
            entry.StartSeconds,
            entry.CreatedAtUtc,
            audioStreamIndex,
            qualityCap);
    }

    public HlsPlaybackAsset? GetAsset(
        Guid sessionId,
        Guid episodeId,
        string profileId,
        string fileName)
    {
        ThrowIfDisposed();
        CleanupExpired();

        if (!sessions.TryGetValue(sessionId, out var entry) ||
            entry.EpisodeId != episodeId ||
            !string.Equals(entry.ProfileId, profileId, StringComparison.Ordinal) ||
            !AllowedAsset(fileName))
        {
            return null;
        }

        var path = Path.Combine(entry.DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        entry.Touch();

        return new HlsPlaybackAsset(
            path,
            ContentType(fileName),
            fileName != "index.m3u8");
    }

    public void CleanupExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - IdleLifetime;
        foreach (var entry in sessions.Values)
        {
            if (entry.LastAccessUtc < cutoff)
            {
                Remove(entry.SessionId);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var sessionId in sessions.Keys.ToArray())
        {
            Remove(sessionId);
        }
    }

    /// <summary>
    /// HLS fallback always encodes H.264, so the quality cap always applies
    /// here; <paramref name="audioStreamIndex"/> keeps the caller's audio
    /// track selection across the fallback restart.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(
        string sourcePath,
        string directory,
        double startSeconds,
        int? audioStreamIndex = null,
        PlaybackQualityCap qualityCap = PlaybackQualityCap.Auto)
    {
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds));
        }

        if (audioStreamIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audioStreamIndex));
        }

        var playlistPath = Path.Combine(directory, "index.m3u8");
        var segmentPath = Path.Combine(directory, "segment-%05d.m4s");

        var arguments = new List<string>
        {
            "-v", "error",
            "-nostdin",
            "-y",
            "-fflags", "+genpts"
        };

        if (startSeconds > 0)
        {
            arguments.AddRange([
                "-ss",
                startSeconds.ToString("0.###", CultureInfo.InvariantCulture)
            ]);
        }

        arguments.AddRange([
            "-i", Path.GetFullPath(sourcePath),
            "-map", "0:v:0",
            "-map", LivePlaybackCommand.AudioMap(audioStreamIndex),
            "-sn",
            "-dn",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "22",
            "-pix_fmt", "yuv420p"
        ]);
        arguments.AddRange(PlaybackQuality.EncodeArguments(qualityCap));
        arguments.AddRange([
            "-force_key_frames", $"expr:gte(t,n_forced*{SegmentSeconds})",
            "-c:a", "aac",
            "-b:a", PlaybackQuality.AudioBitrate(qualityCap),
            "-max_muxing_queue_size", "2048",
            "-avoid_negative_ts", "make_zero",
            "-f", "hls",
            "-hls_time", SegmentSeconds.ToString(CultureInfo.InvariantCulture),
            "-hls_list_size", PlaylistSegments.ToString(CultureInfo.InvariantCulture),
            "-hls_delete_threshold", "3",
            "-hls_segment_type", "fmp4",
            "-hls_fmp4_init_filename", "init.mp4",
            "-hls_flags", "delete_segments+independent_segments",
            "-hls_segment_filename", segmentPath,
            playlistPath
        ]);

        return arguments;
    }

    private static bool AllowedAsset(string fileName) =>
        string.Equals(fileName, "index.m3u8", StringComparison.Ordinal) ||
        string.Equals(fileName, "init.mp4", StringComparison.Ordinal) ||
        SegmentPattern.IsMatch(fileName);

    private static string ContentType(string fileName) =>
        fileName switch
        {
            "index.m3u8" => "application/vnd.apple.mpegurl",
            "init.mp4" => "video/mp4",
            _ => "video/iso.segment"
        };

    private Process StartProcess(
        string sourcePath,
        string directory,
        double startSeconds,
        int? audioStreamIndex,
        PlaybackQualityCap qualityCap)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        foreach (var argument in BuildArguments(
                     sourcePath,
                     directory,
                     startSeconds,
                     audioStreamIndex,
                     qualityCap))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Could not start HLS fallback transcoding.");
        }

        _ = DrainErrorsAsync(process);
        return process;
    }

    private async Task WaitForPlaylistAsync(
        Entry entry,
        CancellationToken cancellationToken)
    {
        var playlist = Path.Combine(entry.DirectoryPath, "index.m3u8");
        var init = Path.Combine(entry.DirectoryPath, "init.mp4");
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(playlist) &&
                File.Exists(init) &&
                new FileInfo(playlist).Length > 0)
            {
                entry.Touch();
                return;
            }

            if (entry.Process.HasExited)
            {
                throw new InvalidOperationException(
                    "HLS fallback transcoder exited before producing a playlist.");
            }

            await Task.Delay(75, cancellationToken);
        }

        throw new TimeoutException(
            "HLS fallback did not produce its first segment in time.");
    }

    private async Task DrainErrorsAsync(Process process)
    {
        try
        {
            var error = await process.StandardError.ReadToEndAsync();
            _ = error;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private void EvictForProfileCapacity(string profileId)
    {
        while (sessions.Values.Count(
                   x => string.Equals(
                       x.ProfileId,
                       profileId,
                       StringComparison.Ordinal)) >= MaxSessionsPerProfile)
        {
            var oldest = sessions.Values
                .Where(x => string.Equals(
                    x.ProfileId,
                    profileId,
                    StringComparison.Ordinal))
                .OrderBy(x => x.LastAccessUtc)
                .FirstOrDefault();

            if (oldest is null)
            {
                break;
            }

            Remove(oldest.SessionId);
        }
    }

    private void Remove(Guid sessionId)
    {
        if (!sessions.TryRemove(sessionId, out var entry))
        {
            return;
        }

        try
        {
            if (!entry.Process.HasExited)
            {
                entry.Process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            entry.Process.Dispose();
        }

        try
        {
            if (Directory.Exists(entry.DirectoryPath))
            {
                Directory.Delete(entry.DirectoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed class Entry(
        Guid sessionId,
        Guid episodeId,
        string profileId,
        string directoryPath,
        Process process,
        double startSeconds,
        DateTimeOffset createdAtUtc)
    {
        public Guid SessionId { get; } = sessionId;
        public Guid EpisodeId { get; } = episodeId;
        public string ProfileId { get; } = profileId;
        public string DirectoryPath { get; } = directoryPath;
        public Process Process { get; } = process;
        public double StartSeconds { get; } = startSeconds;
        public DateTimeOffset CreatedAtUtc { get; } = createdAtUtc;
        public DateTimeOffset LastAccessUtc { get; private set; } = createdAtUtc;

        public void Touch() => LastAccessUtc = DateTimeOffset.UtcNow;
    }
}

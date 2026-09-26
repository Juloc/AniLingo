using System.Collections.Concurrent;
using System.Text.Json;

namespace AniLingo.Web.Features.MediaSegments;

// Cross-episode OP/ED detector (#135): decodes the first and last few minutes of each episode's
// audio, hashes them into a compact per-frame fingerprint (AudioFingerprint), and looks for a
// segment shared with sibling episodes of the same anime and season (AudioFingerprintPolicy).
//
// Not included: recap/preview detection (recaps vary too much in placement and length for this
// window-based approach to bound reliably) and video-based fingerprints; see docs/MEDIA_SEGMENTS.md.
public sealed class AudioFingerprintMediaSegmentDetector(
    IAudioWindowDecoder decoder,
    ILogger<AudioFingerprintMediaSegmentDetector> logger,
    string? cacheRoot = null,
    TimeSpan? introWindow = null,
    TimeSpan? outroWindow = null)
    : IMediaSegmentDetector
{
    public const string DefaultCacheRoot = "/data/media-segment-cache/fingerprints";
    public const int FingerprintVersion = 1;

    // Cross-episode alignment is quadratic in the number of siblings actually compared; this
    // keeps one detection call bounded even for long-running shows. Fingerprints are cached per
    // episode, so a later re-run for a different episode of the same season is cheap regardless.
    public const int MaxSiblingsPerRun = 24;

    public static readonly TimeSpan DefaultIntroWindow = TimeSpan.FromMinutes(6);
    public static readonly TimeSpan DefaultOutroWindow = TimeSpan.FromMinutes(6);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, EpisodeFingerprint> memoryCache = new();

    public string Method => "audio-fingerprint";

    public string Version => FingerprintVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string CacheRoot { get; } = cacheRoot ?? DefaultCacheRoot;

    // Overridable (tests use short windows to keep synthetic PCM small); production leaves the
    // 6-minute defaults, which comfortably covers typical cold-open + OP or ED + next-episode
    // preview placements.
    public TimeSpan IntroWindow { get; } = introWindow ?? DefaultIntroWindow;

    public TimeSpan OutroWindow { get; } = outroWindow ?? DefaultOutroWindow;

    public async Task<IReadOnlyList<DetectedMediaSegment>> DetectAsync(
        MediaSegmentDetectionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DurationSeconds is not (> 0 and var duration) || !double.IsFinite(duration))
        {
            return [];
        }

        var own = await GetOrComputeAsync(
            request.MediaFileId,
            request.MediaIdentity,
            request.MediaPath,
            duration,
            cancellationToken);

        if (own is null)
        {
            // ffmpeg or the media file is unavailable: degrade to no detection rather than throw.
            return [];
        }

        var siblings = request.Siblings.Count > MaxSiblingsPerRun
            ? request.Siblings.Take(MaxSiblingsPerRun).ToArray()
            : request.Siblings;

        var siblingFingerprints = new List<EpisodeFingerprint>(siblings.Count);
        foreach (var sibling in siblings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fingerprint = await GetOrComputeAsync(
                sibling.MediaFileId,
                sibling.MediaIdentity,
                sibling.MediaPath,
                sibling.DurationSeconds,
                cancellationToken);

            if (fingerprint is not null)
            {
                siblingFingerprints.Add(fingerprint);
            }
        }

        if (siblingFingerprints.Count == 0)
        {
            return [];
        }

        var results = new List<DetectedMediaSegment>(2);
        var introMatch = AudioFingerprintPolicy.Evaluate(
            MediaSegmentKind.Intro,
            own.IntroHashes,
            own.IntroWindowStartSeconds,
            [.. siblingFingerprints.Select(x => x.IntroHashes)]);
        if (introMatch is not null)
        {
            results.Add(introMatch);
        }

        var outroMatch = AudioFingerprintPolicy.Evaluate(
            MediaSegmentKind.Outro,
            own.OutroHashes,
            own.OutroWindowStartSeconds,
            [.. siblingFingerprints.Select(x => x.OutroHashes)]);
        if (outroMatch is not null)
        {
            results.Add(outroMatch);
        }

        return results;
    }

    private async Task<EpisodeFingerprint?> GetOrComputeAsync(
        Guid mediaFileId,
        string mediaIdentity,
        string mediaPath,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(mediaFileId, out var cached) &&
            string.Equals(cached.MediaIdentity, mediaIdentity, StringComparison.Ordinal))
        {
            return cached;
        }

        var fromDisk = ReadCache(mediaFileId, mediaIdentity);
        if (fromDisk is not null)
        {
            memoryCache[mediaFileId] = fromDisk;
            return fromDisk;
        }

        var introWindowStart = 0d;
        var introWindowLength = Math.Min(IntroWindow.TotalSeconds, durationSeconds / 2);
        var outroWindowLength = Math.Min(OutroWindow.TotalSeconds, durationSeconds / 2);
        var outroWindowStart = Math.Max(introWindowStart + introWindowLength, durationSeconds - outroWindowLength);
        outroWindowLength = durationSeconds - outroWindowStart;

        var introHashes = await DecodeAndHashAsync(mediaPath, introWindowStart, introWindowLength, cancellationToken);
        var outroHashes = await DecodeAndHashAsync(mediaPath, outroWindowStart, outroWindowLength, cancellationToken);

        if (introHashes is null && outroHashes is null)
        {
            logger.LogDebug(
                "No audio fingerprint could be decoded for {Path}; ffmpeg or the media file may be unavailable.",
                mediaPath);
            return null;
        }

        var fingerprint = new EpisodeFingerprint(
            mediaIdentity,
            introWindowStart,
            introHashes ?? [],
            outroWindowStart,
            outroHashes ?? []);

        WriteCache(mediaFileId, mediaIdentity, fingerprint);
        memoryCache[mediaFileId] = fingerprint;
        return fingerprint;
    }

    private async Task<IReadOnlyList<uint>?> DecodeAndHashAsync(
        string mediaPath,
        double startSeconds,
        double lengthSeconds,
        CancellationToken cancellationToken)
    {
        if (lengthSeconds < AudioFingerprintPolicy.MinSegmentSeconds)
        {
            return [];
        }

        var pcm = await decoder.DecodeAsync(mediaPath, startSeconds, lengthSeconds, cancellationToken);
        return pcm is null ? null : AudioFingerprint.ComputeFrameHashes(pcm);
    }

    private string CacheFilePath(Guid mediaFileId, string mediaIdentity) =>
        Path.Combine(CacheRoot, $"{mediaFileId:N}-{mediaIdentity}-v{Version}.json");

    private EpisodeFingerprint? ReadCache(Guid mediaFileId, string mediaIdentity)
    {
        var path = CacheFilePath(mediaFileId, mediaIdentity);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredFingerprint>(File.ReadAllText(path), JsonOptions);
            if (stored is null || !string.Equals(stored.MediaIdentity, mediaIdentity, StringComparison.Ordinal))
            {
                return null;
            }

            return new EpisodeFingerprint(
                stored.MediaIdentity,
                stored.IntroWindowStartSeconds,
                stored.IntroHashes,
                stored.OutroWindowStartSeconds,
                stored.OutroHashes);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Could not read fingerprint cache {Path}; it will be recomputed.",
                path);
            return null;
        }
    }

    private void WriteCache(Guid mediaFileId, string mediaIdentity, EpisodeFingerprint fingerprint)
    {
        try
        {
            Directory.CreateDirectory(CacheRoot);
            PruneSupersededFingerprints(mediaFileId, mediaIdentity);

            var stored = new StoredFingerprint(
                FingerprintVersion,
                mediaIdentity,
                fingerprint.IntroWindowStartSeconds,
                [.. fingerprint.IntroHashes],
                fingerprint.OutroWindowStartSeconds,
                [.. fingerprint.OutroHashes]);
            File.WriteAllText(
                CacheFilePath(mediaFileId, mediaIdentity),
                JsonSerializer.Serialize(stored, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not write fingerprint cache under {Root}.", CacheRoot);
        }
    }

    // Removes older identities/versions of the same media file; the cache is disposable and
    // keyed by media identity + detector version, so nothing is lost by pruning it.
    private void PruneSupersededFingerprints(Guid mediaFileId, string currentMediaIdentity)
    {
        try
        {
            if (!Directory.Exists(CacheRoot))
            {
                return;
            }

            var currentName = Path.GetFileName(CacheFilePath(mediaFileId, currentMediaIdentity));
            foreach (var file in Directory.EnumerateFiles(CacheRoot, $"{mediaFileId:N}-*.json"))
            {
                if (!string.Equals(Path.GetFileName(file), currentName, StringComparison.Ordinal))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not prune superseded fingerprint caches under {Root}.", CacheRoot);
        }
    }

    private sealed record EpisodeFingerprint(
        string MediaIdentity,
        double IntroWindowStartSeconds,
        IReadOnlyList<uint> IntroHashes,
        double OutroWindowStartSeconds,
        IReadOnlyList<uint> OutroHashes);

    private sealed record StoredFingerprint(
        int Version,
        string MediaIdentity,
        double IntroWindowStartSeconds,
        IReadOnlyList<uint> IntroHashes,
        double OutroWindowStartSeconds,
        IReadOnlyList<uint> OutroHashes);
}

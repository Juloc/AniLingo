using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Infrastructure;

namespace AniLingo.Web.Features.MediaSegments;

public enum TrickplayState
{
    Unavailable,
    Queued,
    Generating,
    Ready,
    Failed
}

// index.json inside one trickplay cache directory. Thumbnail i (0-based, at
// i * IntervalMs) lives on sprite floor(i / (Columns * Rows)) at column
// i % Columns and row (i / Columns) % Rows.
public sealed record TrickplayIndex(
    int Version,
    int GeneratorVersion,
    string MediaIdentity,
    int IntervalMs,
    int TileWidth,
    int TileHeight,
    int Columns,
    int Rows,
    int ThumbnailCount,
    IReadOnlyList<string> Sprites);

public sealed record TrickplayDescriptor(
    TrickplayState State,
    string? Message,
    TrickplayIndex? Index)
{
    public static TrickplayDescriptor Unavailable { get; } =
        new(TrickplayState.Unavailable, null, null);

    public bool IsReady => State == TrickplayState.Ready && Index is not null;
}

public sealed record TrickplayRequest(
    Guid EpisodeId,
    Guid MediaFileId,
    string MediaIdentity,
    string SourcePath,
    double DurationSeconds,
    string Subject);

public sealed record TrickplayAsset(
    string Path,
    string ContentType);

// Generates bounded timeline sprite sheets with ffmpeg into a disposable cache
// under /data, keyed by media identity and generator version. Generation runs as
// a maintenance operation and never blocks playback; source media is only read.
public sealed class TrickplayGenerator(
    MediaProcessRunner processRunner,
    BackgroundJobQueue jobs,
    ILogger<TrickplayGenerator> logger,
    string? rootPath = null,
    string ffmpegExecutable = "ffmpeg")
{
    public const int GeneratorVersion = 1;
    public const int IndexVersion = 1;
    public const string DefaultRootPath = "/data/playback-cache/trickplay";
    public const string IndexFileName = "index.json";
    public const string OperationKind = "trickplay-generation";
    public const int TileWidth = 320;
    public const int TileHeight = 180;
    public const int Columns = 10;
    public const int Rows = 10;
    public const int MinimumIntervalSeconds = 10;
    public const int MaximumThumbnails = 720;
    public const int MaximumPendingGenerations = 2;
    public static readonly TimeSpan GenerationTimeout = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PendingExpiry = TimeSpan.FromHours(2);

    private static readonly Regex AssetPattern = new(
        "^(index\\.json|sprite-[0-9]{3}\\.jpg)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ConcurrentDictionary<string, RuntimeState> runtime =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, TrickplayIndex> indexCache =
        new(StringComparer.Ordinal);

    public string RootPath { get; } = rootPath ?? DefaultRootPath;

    // One directory per media file, source identity and generator version. The media
    // file prefix lets a new generation prune the superseded ones of the same file,
    // so the cache holds at most one generation per media file.
    public static string CacheKey(Guid mediaFileId, string mediaIdentity) =>
        $"{MediaFilePrefix(mediaFileId)}{mediaIdentity}-v{GeneratorVersion}";

    public string CacheDirectory(Guid mediaFileId, string mediaIdentity) =>
        Path.Combine(RootPath, CacheKey(mediaFileId, mediaIdentity));

    private static string MediaFilePrefix(Guid mediaFileId) =>
        $"{mediaFileId:N}-";

    public static bool IsAllowedAssetName(string fileName) =>
        AssetPattern.IsMatch(fileName);

    public static int ComputeIntervalSeconds(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            return MinimumIntervalSeconds;
        }

        return Math.Max(
            MinimumIntervalSeconds,
            (int)Math.Ceiling(durationSeconds / MaximumThumbnails));
    }

    public static int ComputeThumbnailCount(double durationSeconds, int intervalSeconds) =>
        Math.Max(1, (int)Math.Ceiling(durationSeconds / intervalSeconds));

    public static IReadOnlyList<string> BuildFfmpegArguments(
        string sourcePath,
        string outputPattern,
        int intervalSeconds) =>
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-skip_frame", "nokey",
            "-i", sourcePath,
            "-an", "-sn", "-dn",
            "-vf",
            string.Create(
                CultureInfo.InvariantCulture,
                $"fps=1/{intervalSeconds},scale={TileWidth}:{TileHeight}:force_original_aspect_ratio=decrease,pad={TileWidth}:{TileHeight}:(ow-iw)/2:(oh-ih)/2:color=black,tile={Columns}x{Rows}"),
            "-q:v", "5",
            "-f", "image2",
            outputPattern
        ];

    public TrickplayDescriptor Describe(Guid mediaFileId, string mediaIdentity)
    {
        var index = ReadIndex(mediaFileId, mediaIdentity);
        if (index is not null)
        {
            return new TrickplayDescriptor(TrickplayState.Ready, null, index);
        }

        return runtime.TryGetValue(CacheKey(mediaFileId, mediaIdentity), out var state) &&
               (state.IsPending || state.State == TrickplayState.Failed)
            ? new TrickplayDescriptor(state.State, state.Message, null)
            : TrickplayDescriptor.Unavailable;
    }

    public TrickplayAsset? GetAsset(Guid mediaFileId, string mediaIdentity, string fileName)
    {
        if (!IsAllowedAssetName(fileName) || ReadIndex(mediaFileId, mediaIdentity) is null)
        {
            return null;
        }

        var path = Path.Combine(CacheDirectory(mediaFileId, mediaIdentity), fileName);
        return File.Exists(path)
            ? new TrickplayAsset(
                path,
                fileName == IndexFileName ? "application/json" : "image/jpeg")
            : null;
    }

    // Queues generation once per cache key. A previous failure is not retried
    // automatically; RegenerateAsync (owner action) or a restart clears it.
    public async Task<bool> EnsureQueuedAsync(
        TrickplayRequest request,
        CancellationToken cancellationToken)
    {
        if (ReadIndex(request.MediaFileId, request.MediaIdentity) is not null ||
            request.DurationSeconds <= 0 ||
            !double.IsFinite(request.DurationSeconds))
        {
            return false;
        }

        var key = CacheKey(request.MediaFileId, request.MediaIdentity);
        var known = runtime.TryGetValue(key, out var existing);
        if ((known && (existing!.IsPending || existing.State == TrickplayState.Failed)) ||
            PendingCount() >= MaximumPendingGenerations)
        {
            // Already queued or failed for this identity, or enough work is pending:
            // a later player load queues it.
            return false;
        }

        var queued = new RuntimeState(TrickplayState.Queued, null);
        if (known ? !runtime.TryUpdate(key, queued, existing!) : !runtime.TryAdd(key, queued))
        {
            return false;
        }

        // The shared job queue is bounded; a player request must never wait for it.
        using var enqueueTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        enqueueTimeout.CancelAfter(EnqueueTimeout);

        try
        {
            await jobs.QueueAsync(
                new OperationDescriptor(
                    OperationKind,
                    "Playback",
                    "Generate seek preview thumbnails",
                    request.Subject,
                    Lane: OperationLane.Maintenance,
                    Retryable: false),
                (operation, _, token) => GenerateAsync(request, operation, token),
                enqueueTimeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            runtime.TryRemove(key, out _);
            logger.LogDebug(
                "Background queue busy; seek preview generation for {Subject} was not queued.",
                request.Subject);
            return false;
        }
        catch
        {
            runtime.TryRemove(key, out _);
            throw;
        }
    }

    private int PendingCount() =>
        runtime.Values.Count(x => x.IsPending);

    public async Task<bool> RegenerateAsync(
        TrickplayRequest request,
        CancellationToken cancellationToken)
    {
        var key = CacheKey(request.MediaFileId, request.MediaIdentity);
        if (runtime.TryGetValue(key, out var state) && state.IsPending)
        {
            return false;
        }

        runtime.TryRemove(key, out _);
        indexCache.TryRemove(key, out _);
        DeleteDirectory(CacheDirectory(request.MediaFileId, request.MediaIdentity));
        return await EnsureQueuedAsync(request, cancellationToken);
    }

    public async Task GenerateAsync(
        TrickplayRequest request,
        OperationExecutionContext operation,
        CancellationToken cancellationToken)
    {
        var key = CacheKey(request.MediaFileId, request.MediaIdentity);
        runtime[key] = new RuntimeState(TrickplayState.Generating, null);

        try
        {
            if (ReadIndex(request.MediaFileId, request.MediaIdentity) is not null)
            {
                runtime.TryRemove(key, out _);
                return;
            }

            if (!File.Exists(request.SourcePath))
            {
                throw new InvalidOperationException(
                    "The media file is not readable, so no seek preview was generated.");
            }

            await operation.ReportAsync(
                5,
                "Extracting timeline thumbnails with ffmpeg.",
                cancellationToken: cancellationToken);

            var intervalSeconds = ComputeIntervalSeconds(request.DurationSeconds);
            var thumbnailCount = ComputeThumbnailCount(request.DurationSeconds, intervalSeconds);
            var finalDirectory = CacheDirectory(request.MediaFileId, request.MediaIdentity);
            var temporaryDirectory = Path.Combine(
                RootPath,
                ".tmp",
                $"{key}-{Guid.NewGuid():N}");

            RemoveAbandonedTemporaryDirectories();
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                var result = await processRunner.RunAsync(
                    ffmpegExecutable,
                    BuildFfmpegArguments(
                        request.SourcePath,
                        Path.Combine(temporaryDirectory, "sprite-%03d.jpg"),
                        intervalSeconds),
                    GenerationTimeout,
                    cancellationToken);

                if (result is null)
                {
                    throw new InvalidOperationException(
                        "ffmpeg is unavailable or timed out; seek preview generation was skipped.");
                }

                if (result.ExitCode != 0)
                {
                    logger.LogWarning(
                        "ffmpeg trickplay generation failed for {Subject}: {Error}",
                        request.Subject,
                        result.ErrorSummary);
                    throw new InvalidOperationException(
                        "ffmpeg could not extract timeline thumbnails from this media file.");
                }

                var sprites = Directory
                    .EnumerateFiles(temporaryDirectory, "sprite-*.jpg")
                    .Select(Path.GetFileName)
                    .Where(name => name is not null && IsAllowedAssetName(name))
                    .Select(name => name!)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                if (sprites.Length == 0)
                {
                    throw new InvalidOperationException(
                        "ffmpeg produced no timeline thumbnails for this media file.");
                }

                var index = new TrickplayIndex(
                    IndexVersion,
                    GeneratorVersion,
                    request.MediaIdentity,
                    intervalSeconds * 1000,
                    TileWidth,
                    TileHeight,
                    Columns,
                    Rows,
                    Math.Min(thumbnailCount, sprites.Length * Columns * Rows),
                    sprites);

                await File.WriteAllTextAsync(
                    Path.Combine(temporaryDirectory, IndexFileName),
                    JsonSerializer.Serialize(index, JsonOptions),
                    cancellationToken);

                await operation.ReportAsync(
                    95,
                    "Publishing seek preview cache.",
                    cancellationToken: cancellationToken);

                Directory.CreateDirectory(RootPath);
                DeleteDirectory(finalDirectory);
                Directory.Move(temporaryDirectory, finalDirectory);
                indexCache[key] = index;
                runtime.TryRemove(key, out _);
                PruneSupersededGenerations(request.MediaFileId, key);

                logger.LogInformation(
                    "Generated {Count} seek preview thumbnails ({Interval}s interval, {Sprites} sprite sheets) for {Subject}.",
                    index.ThumbnailCount,
                    intervalSeconds,
                    sprites.Length,
                    request.Subject);
            }
            finally
            {
                DeleteDirectory(temporaryDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            runtime.TryRemove(key, out _);
            throw;
        }
        catch (Exception exception)
        {
            runtime[key] = new RuntimeState(
                TrickplayState.Failed,
                exception is InvalidOperationException
                    ? exception.Message
                    : "Seek preview generation failed; see the operation log.");
            throw;
        }
    }

    private TrickplayIndex? ReadIndex(Guid mediaFileId, string mediaIdentity)
    {
        var key = CacheKey(mediaFileId, mediaIdentity);
        var path = Path.Combine(CacheDirectory(mediaFileId, mediaIdentity), IndexFileName);

        // The cache is disposable: an operator may delete it at any time.
        if (!File.Exists(path))
        {
            indexCache.TryRemove(key, out _);
            return null;
        }

        if (indexCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var index = JsonSerializer.Deserialize<TrickplayIndex>(
                File.ReadAllText(path),
                JsonOptions);

            if (index is null ||
                index.GeneratorVersion != GeneratorVersion ||
                !string.Equals(index.MediaIdentity, mediaIdentity, StringComparison.Ordinal) ||
                index.IntervalMs <= 0 ||
                index.Columns <= 0 ||
                index.Rows <= 0 ||
                index.ThumbnailCount <= 0 ||
                index.Sprites.Count == 0 ||
                index.Sprites.Any(name => !IsAllowedAssetName(name)))
            {
                logger.LogWarning(
                    "Ignoring inconsistent trickplay cache {Directory}; it will be regenerated on demand.",
                    CacheDirectory(mediaFileId, mediaIdentity));
                DeleteDirectory(CacheDirectory(mediaFileId, mediaIdentity));
                return null;
            }

            indexCache[key] = index;
            return index;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Could not read trickplay cache {Directory}; it will be regenerated on demand.",
                CacheDirectory(mediaFileId, mediaIdentity));
            return null;
        }
    }

    // Work directories left behind by a crash or restart during generation.
    private void RemoveAbandonedTemporaryDirectories()
    {
        var temporaryRoot = Path.Combine(RootPath, ".tmp");
        try
        {
            if (!Directory.Exists(temporaryRoot))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(temporaryRoot))
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(directory) > PendingExpiry)
                {
                    DeleteDirectory(directory);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Could not clean abandoned trickplay work directories under {Directory}.",
                temporaryRoot);
        }
    }

    // Removes older identities/generator versions of the same media file.
    private void PruneSupersededGenerations(Guid mediaFileId, string currentKey)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(RootPath, $"{MediaFilePrefix(mediaFileId)}*"))
            {
                var name = Path.GetFileName(directory);
                if (!string.Equals(name, currentKey, StringComparison.Ordinal))
                {
                    indexCache.TryRemove(name, out _);
                    DeleteDirectory(directory);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Could not prune superseded trickplay caches under {Directory}.",
                RootPath);
        }
    }

    private void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Could not remove trickplay cache directory {Directory}.",
                path);
        }
    }

    private sealed record RuntimeState(TrickplayState State, string? Message)
    {
        public DateTime SinceUtc { get; } = DateTime.UtcNow;

        // A queued job that was cancelled before it ran (or lost) must not block
        // generation until the next restart.
        public bool IsPending =>
            State is TrickplayState.Queued or TrickplayState.Generating &&
            DateTime.UtcNow - SinceUtc < PendingExpiry;
    }
}

using System.Collections.Concurrent;
using Jularr.Web.Data;

namespace Jularr.Web.Features.Artwork;

/// <summary>Remote provider artwork of an anime (AniList cover and banner).</summary>
public sealed record AnimeProviderArtwork(string? PosterUrl, string? BannerUrl);

public sealed record AnimeArtworkReconcileResult(
    int Refreshed,
    int Unchanged,
    int Missing,
    int Migrated,
    bool Deferred)
{
    public static AnimeArtworkReconcileResult StorageUnavailable { get; } = new(0, 0, 0, 0, true);
}

public enum AnimeArtworkPersistOutcome
{
    Saved,
    Current,
    KeptCustom,
    Unavailable,
    Rejected,
    Failed
}

/// <summary>
/// The one owner of durable anime artwork. Canonical files live beside the media on the library
/// storage (series folder, season folders); the local cache only holds derivatives.
/// Precedence per image: the user's own files beside the media, then provider artwork Jularr
/// persisted there (Sonarr over migrated copies over AniList), then the remote provider URL as a
/// page-level fallback. Nothing is written while the media folder is missing (unavailable NAS),
/// and a file Jularr did not write, or that changed since it wrote it, is never replaced.
/// </summary>
public sealed class AnimeArtworkLibrary(
    AppDbContext db,
    ILogger<AnimeArtworkLibrary> logger,
    IHttpClientFactory? httpClientFactory = null,
    AnimeArtworkCache? cache = null)
{
    public const string HttpClientName = "artwork";

    // A read-only or failing folder is not retried (and its artwork not downloaded again) on
    // every scan.
    private static readonly TimeSpan WriteFailureBackoff = TimeSpan.FromHours(6);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> WriteFailures = new(StringComparer.Ordinal);

    private readonly MediaArtworkAssetStore assets = new(db);

    public AnimeArtworkCache Cache { get; } = cache ?? AnimeArtworkCache.Default;

    /// <summary>
    /// Brings one anime's artwork up to date after its media folder was scanned: migrates a
    /// legacy local copy beside the media, persists missing provider artwork, and rebuilds the
    /// derivatives. <paramref name="seasons"/> maps each season to its own folder (null when the
    /// episodes sit in the series folder).
    /// </summary>
    public async Task<AnimeArtworkReconcileResult> ReconcileAsync(
        Guid animeId,
        string seriesDirectory,
        IReadOnlyDictionary<int, string?> seasons,
        AnimeProviderArtwork? provider,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(seriesDirectory))
        {
            return AnimeArtworkReconcileResult.StorageUnavailable;
        }

        try
        {
            var migrated = await MigrateLegacyAsync(animeId, seriesDirectory, cancellationToken);
            if (provider is not null)
            {
                await PersistRemoteAsync(animeId, seriesDirectory, AnimeArtworkKind.Poster, provider.PosterUrl, cancellationToken);
                await PersistRemoteAsync(animeId, seriesDirectory, AnimeArtworkKind.Banner, provider.BannerUrl, cancellationToken);
            }

            var records = await LoadAsync(animeId, cancellationToken);
            var refreshed = 0;
            var unchanged = 0;
            var missing = 0;
            foreach (var slot in new[] { AnimeArtworkSlot.Poster, AnimeArtworkSlot.Fanart })
            {
                var canonical = ResolveCanonical(seriesDirectory, slot.Kind, records);
                if (canonical is null)
                {
                    Cache.Remove(animeId, slot);
                    missing++;
                }
                else if (await Cache.RefreshAsync(animeId, slot, canonical, cancellationToken))
                {
                    refreshed++;
                }
                else
                {
                    unchanged++;
                }
            }

            foreach (var (seasonNumber, seasonDirectory) in seasons)
            {
                if (seasonDirectory is not null && !Directory.Exists(seasonDirectory))
                {
                    continue;
                }

                var slot = AnimeArtworkSlot.SeasonPoster(seasonNumber);
                var canonical = AnimeArtworkFiles
                    .FindSeasonCandidates(seriesDirectory, seasonDirectory, seasonNumber, AnimeArtworkKind.Poster)
                    .FirstOrDefault();
                if (canonical is null)
                {
                    Cache.Remove(animeId, slot);
                }
                else if (await Cache.RefreshAsync(animeId, slot, canonical, cancellationToken))
                {
                    refreshed++;
                }
            }

            return new AnimeArtworkReconcileResult(refreshed, unchanged, missing, migrated, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreachable or failing NAS mid-way leaves every file, row and derivative as it was.
            logger.LogWarning(exception, "Artwork of anime {AnimeId} could not be reconciled; it is retried on the next scan.", animeId);
            return AnimeArtworkReconcileResult.StorageUnavailable;
        }
    }

    /// <summary>
    /// Decides, before anything is downloaded, whether provider artwork from
    /// <paramref name="source"/> would be written beside the media.
    /// </summary>
    public async Task<bool> ShouldPersistAsync(
        Guid animeId,
        string seriesDirectory,
        AnimeArtworkKind kind,
        string source,
        string? identity,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(seriesDirectory) || InBackoff(seriesDirectory))
        {
            return false;
        }

        var records = await LoadAsync(animeId, cancellationToken);
        return Decide(seriesDirectory, kind, records.GetValueOrDefault(kind), source, identity) == Decision.Write;
    }

    /// <summary>Writes provider artwork beside the media unless user or better artwork is already there.</summary>
    public async Task<AnimeArtworkPersistOutcome> PersistProviderAsync(
        Guid animeId,
        string seriesDirectory,
        AnimeArtworkKind kind,
        byte[] bytes,
        string source,
        string? identity,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(seriesDirectory))
        {
            return AnimeArtworkPersistOutcome.Unavailable;
        }

        var extension = bytes.Length > AnimeArtworkFiles.MaxImageBytes
            ? null
            : AnimeArtworkFiles.DetectExtension(bytes);
        if (extension is null)
        {
            return AnimeArtworkPersistOutcome.Rejected;
        }

        var records = await LoadAsync(animeId, cancellationToken);
        var record = records.GetValueOrDefault(kind);
        switch (Decide(seriesDirectory, kind, record, source, identity))
        {
            case Decision.KeptCustom:
                return AnimeArtworkPersistOutcome.KeptCustom;
            case Decision.Current:
                return AnimeArtworkPersistOutcome.Current;
        }

        var fileName = AnimeArtworkFiles.BaseName(kind) + extension;
        var path = Path.Combine(seriesDirectory, fileName);
        var previous = record is not null && IsIntact(seriesDirectory, record) ? record : null;
        try
        {
            if (!await WriteVerifiedAsync(path, bytes, cancellationToken))
            {
                return AnimeArtworkPersistOutcome.Failed;
            }

            // Replacing poster.jpg with a PNG must not leave the old poster.jpg behind.
            if (previous is not null &&
                !string.Equals(previous.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                AnimeArtworkFiles.TryDelete(Path.Combine(seriesDirectory, previous.FileName));
            }

            await RecordAsync(animeId, kind, path, source, identity, cancellationToken);
            WriteFailures.TryRemove(seriesDirectory, out _);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteFailures[seriesDirectory] = DateTimeOffset.UtcNow;
            logger.LogWarning(exception, "Artwork could not be written to {Path}.", path);
            return AnimeArtworkPersistOutcome.Failed;
        }

        var slot = kind == AnimeArtworkKind.Poster ? AnimeArtworkSlot.Poster : AnimeArtworkSlot.Fanart;
        var canonical = ResolveCanonical(seriesDirectory, slot.Kind, await LoadAsync(animeId, cancellationToken));
        if (canonical is not null)
        {
            await Cache.RefreshAsync(animeId, slot, canonical, cancellationToken);
        }

        return AnimeArtworkPersistOutcome.Saved;
    }

    /// <summary>Anime artwork folders still waiting under the legacy local store.</summary>
    public int CountLegacyFolders() =>
        Directory.Exists(Cache.LegacyRootPath)
            ? Directory.EnumerateDirectories(Cache.LegacyRootPath).Count()
            : 0;

    /// <summary>
    /// Moves artwork a previous version kept under /data/artwork/anime beside the media. A copy
    /// is only written where the media folder has no artwork of that kind yet (the user's own
    /// file always wins), is verified byte for byte, recorded, and only then is the local copy
    /// removed. Anything that fails keeps the local copy for the next run.
    /// </summary>
    private async Task<int> MigrateLegacyAsync(
        Guid animeId,
        string seriesDirectory,
        CancellationToken cancellationToken)
    {
        var legacyDirectory = Cache.LegacyDirectory(animeId);
        if (!Directory.Exists(legacyDirectory))
        {
            return 0;
        }

        var migrated = 0;
        foreach (var kind in new[] { AnimeArtworkKind.Poster, AnimeArtworkKind.Fanart })
        {
            var legacyPath = Cache.FindLegacyPath(animeId, kind);
            if (legacyPath is null)
            {
                continue;
            }

            if (AnimeArtworkFiles.FindDisplayCandidates(seriesDirectory, kind).Count == 0)
            {
                var bytes = await File.ReadAllBytesAsync(legacyPath, cancellationToken);
                var extension = AnimeArtworkFiles.DetectExtension(bytes);
                if (extension is null)
                {
                    logger.LogWarning("Legacy artwork {Path} is not a readable image and was left in place.", legacyPath);
                    continue;
                }

                var path = Path.Combine(seriesDirectory, AnimeArtworkFiles.BaseName(kind) + extension);
                if (!await WriteVerifiedAsync(path, bytes, cancellationToken))
                {
                    logger.LogWarning("Legacy artwork {Path} could not be verified at {Target}; the local copy is kept.", legacyPath, path);
                    continue;
                }

                await RecordAsync(animeId, kind, path, MediaArtworkSources.Migrated, null, cancellationToken);
                migrated++;
            }

            var baseName = AnimeArtworkFiles.BaseName(kind);
            foreach (var file in Directory.EnumerateFiles(legacyDirectory)
                         .Where(file => Path.GetFileName(file).StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase)))
            {
                AnimeArtworkFiles.TryDelete(file);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(legacyDirectory).Any())
        {
            Directory.Delete(legacyDirectory);
        }

        return migrated;
    }

    private async Task PersistRemoteAsync(
        Guid animeId,
        string seriesDirectory,
        AnimeArtworkKind kind,
        string? url,
        CancellationToken cancellationToken)
    {
        if (httpClientFactory is null ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !await ShouldPersistAsync(animeId, seriesDirectory, kind, MediaArtworkSources.AniList, url, cancellationToken))
        {
            return;
        }

        byte[]? bytes;
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            bytes = await AnimeArtworkFiles.DownloadImageAsync(client, uri, cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException ||
            (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogDebug(exception, "Provider artwork {Url} could not be downloaded.", url);
            return;
        }

        if (bytes is not null)
        {
            await PersistProviderAsync(animeId, seriesDirectory, kind, bytes, MediaArtworkSources.AniList, url, cancellationToken);
        }
    }

    private enum Decision
    {
        Write,
        Current,
        KeptCustom
    }

    private static Decision Decide(
        string seriesDirectory,
        AnimeArtworkKind kind,
        MediaArtworkAsset? record,
        string source,
        string? identity)
    {
        var managed = record is not null && IsIntact(seriesDirectory, record) ? record : null;
        var custom = AnimeArtworkFiles.FindFiles(seriesDirectory, kind).Any(file =>
            managed is null ||
            !string.Equals(Path.GetFileName(file), managed.FileName, StringComparison.OrdinalIgnoreCase));
        if (custom)
        {
            return Decision.KeptCustom;
        }

        if (managed is null)
        {
            return Decision.Write;
        }

        if (MediaArtworkSources.Rank(source) < MediaArtworkSources.Rank(managed.Source))
        {
            return Decision.Current;
        }

        return string.Equals(source, managed.Source, StringComparison.Ordinal) &&
               (identity is null || string.Equals(identity, managed.SourceIdentity, StringComparison.Ordinal))
            ? Decision.Current
            : Decision.Write;
    }

    // The user's own file first, then what Jularr persisted there.
    private static string? ResolveCanonical(
        string seriesDirectory,
        AnimeArtworkKind kind,
        IReadOnlyDictionary<AnimeArtworkKind, MediaArtworkAsset> records)
    {
        var managed = records.Values
            .Where(record => IsIntact(seriesDirectory, record))
            .Select(record => record.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = AnimeArtworkFiles.FindDisplayCandidates(seriesDirectory, kind);
        return candidates.FirstOrDefault(path => !managed.Contains(Path.GetFileName(path))) ??
               candidates.FirstOrDefault();
    }

    private static bool IsIntact(string seriesDirectory, MediaArtworkAsset record) =>
        record.Matches(new FileInfo(Path.Combine(seriesDirectory, record.FileName)));

    private static async Task<bool> WriteVerifiedAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await AnimeArtworkFiles.WriteAtomicAsync(path, bytes, cancellationToken);
        if (await AnimeArtworkFiles.ContentEqualsAsync(path, bytes, cancellationToken))
        {
            return true;
        }

        AnimeArtworkFiles.TryDelete(path);
        return false;
    }

    private Task RecordAsync(
        Guid animeId,
        AnimeArtworkKind kind,
        string path,
        string source,
        string? identity,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        return assets.UpsertAsync(
            new MediaArtworkAsset(
                MediaArtworkScopes.AnimeSeries,
                animeId,
                0,
                AnimeArtworkFiles.BaseName(kind),
                file.Name,
                source,
                identity,
                file.Length,
                file.LastWriteTimeUtc),
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<AnimeArtworkKind, MediaArtworkAsset>> LoadAsync(
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var rows = await assets.ListAsync(MediaArtworkScopes.AnimeSeries, animeId, cancellationToken);
        var result = new Dictionary<AnimeArtworkKind, MediaArtworkAsset>();
        foreach (var row in rows)
        {
            foreach (var kind in Enum.GetValues<AnimeArtworkKind>())
            {
                if (string.Equals(row.Kind, AnimeArtworkFiles.BaseName(kind), StringComparison.Ordinal))
                {
                    result[kind] = row;
                }
            }
        }

        return result;
    }

    private static bool InBackoff(string seriesDirectory) =>
        WriteFailures.TryGetValue(seriesDirectory, out var failedAt) &&
        DateTimeOffset.UtcNow - failedAt < WriteFailureBackoff;
}

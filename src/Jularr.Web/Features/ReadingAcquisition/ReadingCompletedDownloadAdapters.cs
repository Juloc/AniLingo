using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Manga;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Novels;

namespace Jularr.Web.Features.ReadingAcquisition;

public sealed class MangaCompletedDownloadImportAdapter(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    MediaMappingReviewStore mappingReviewStore,
    ReadingSegmentMappingStore segmentMappings,
    AnimeImportSettingsStore importSettings,
    IHardLinkCreator hardLinks,
    ILogger<MangaCompletedDownloadImportAdapter> logger,
    string? mangaCacheRoot = null)
    : ICompletedDownloadImportAdapter
{
    public MediaAcquisitionKind Kind =>
        MediaAcquisitionKind.Manga;

    public async Task<CompletedDownloadImportResult> ImportAsync(
        CompletedDownloadImportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var repository = new MangaRepository(db);

            // One series per AniList entry: a new volume of a series that is already matched is
            // added to that series instead of creating another one per release (#485 item 7).
            var existing = request.Request.Provider.Equals(
                    NovelAniListProvider.ProviderKey,
                    StringComparison.OrdinalIgnoreCase)
                ? await repository.FindByMetadataAsync(
                    NovelAniListProvider.ProviderKey,
                    request.Request.ExternalId,
                    cancellationToken)
                : null;

            var settings = await importSettings.LoadAsync(cancellationToken);
            var library = settings.LibraryFor(MediaAcquisitionKind.Manga);
            var sourceExists = File.Exists(request.SourcePath) || Directory.Exists(request.SourcePath);

            string importSource;
            if (library is null)
            {
                // No Manga library configured: the completed download is read in place.
                if (!sourceExists)
                {
                    return CompletedDownloadImportResult.RetryLater(
                        "The completed Manga files are not currently available.");
                }

                importSource = request.SourcePath;
            }
            else
            {
                var seriesFolder = MangaLibraryPlacement.SeriesFolder(
                    library.LibraryRoot,
                    existing,
                    request.Request.Title);
                var releaseTarget = Path.Combine(
                    seriesFolder,
                    MangaLibraryPlacement.SafeName(
                        Path.GetFileName(request.SourcePath.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar))));

                if (sourceExists)
                {
                    new MangaLibraryPlacement(new ImportFileTransfer(hardLinks)).Place(
                        request.SourcePath,
                        releaseTarget,
                        settings.ModeFor(MediaAcquisitionKind.Manga));
                }
                else if (!File.Exists(releaseTarget) && !Directory.Exists(releaseTarget))
                {
                    return CompletedDownloadImportResult.RetryLater(
                        "The completed Manga files are not currently available.");
                }

                // A series living elsewhere keeps its folder; only the new release is added.
                importSource = existing is not null &&
                               !MangaLibraryPlacement.SamePath(existing.SourcePath, seriesFolder)
                    ? releaseTarget
                    : seriesFolder;
            }

            var importer = new MangaImportService(repository, mangaCacheRoot);
            var imported = await importer.ImportAsync(
                importSource,
                cancellationToken,
                existing?.Id);

            string? metadataWarning = null;
            if (existing is null &&
                request.Request.Provider.Equals(
                    NovelAniListProvider.ProviderKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var metadata = new MangaAniListService(
                        repository,
                        httpClientFactory,
                        mappingReviewStore,
                        segmentMappings);
                    await metadata.MatchAsync(
                        imported.SeriesId,
                        request.Request.ExternalId,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    HttpRequestException or
                    TaskCanceledException or
                    System.Text.Json.JsonException)
                {
                    logger.LogWarning(
                        exception,
                        "AniList reconciliation failed after Manga request {RequestId} imported.",
                        request.Request.Id);
                    metadataWarning =
                        " Manga was imported, but AniList reconciliation needs attention.";
                }
            }

            return CompletedDownloadImportResult.Completed(
                $"Imported {imported.ChapterCount} Manga chapter(s).{metadataWarning}",
                $"/Manga/Series/{imported.SeriesId}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CrossDeviceLinkException exception)
        {
            logger.LogWarning(
                exception,
                "Manga hardlink import crosses filesystems for request {RequestId}.",
                request.Request.Id);
            return CompletedDownloadImportResult.RetryLater(
                "The download and the Manga library are on different filesystems, so a hardlink is impossible. Choose \"Hardlink or copy\", Copy or Move for Manga.");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Manga import is waiting for storage for request {RequestId}.",
                request.Request.Id);
            return CompletedDownloadImportResult.RetryLater(
                "Manga import is waiting for storage.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException)
        {
            logger.LogWarning(
                exception,
                "Downloaded Manga release was unsuitable for request {RequestId}.",
                request.Request.Id);
            return CompletedDownloadImportResult.RejectRelease(
                "Downloaded release could not be imported as Manga.");
        }
    }
}

public sealed class LightNovelCompletedDownloadImportAdapter(
    NovelEpubImportService importer,
    NovelMetadataService metadata,
    ILogger<LightNovelCompletedDownloadImportAdapter> logger)
    : ICompletedDownloadImportAdapter
{
    public MediaAcquisitionKind Kind =>
        MediaAcquisitionKind.LightNovel;

    public async Task<CompletedDownloadImportResult> ImportAsync(
        CompletedDownloadImportRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.SourcePath) &&
            !Directory.Exists(request.SourcePath))
        {
            return CompletedDownloadImportResult.RetryLater(
                "The completed Light Novel files are not currently available.");
        }

        try
        {
            // Recursive, no folder hints, validated before anything is stored (#485 item 8).
            var import = await importer.ImportDownloadAsync(
                request.SourcePath,
                cancellationToken);
            if (import.RejectedBecause is { } rejected)
            {
                return CompletedDownloadImportResult.RejectRelease(
                    $"Downloaded Light Novel release was refused: {rejected}");
            }

            var outcomes = import.Outcomes;
            var successes = outcomes
                .Where(outcome =>
                    outcome.Succeeded &&
                    outcome.WorkId is not null)
                .ToArray();
            if (successes.Length == 0)
            {
                return CompletedDownloadImportResult.RejectRelease(
                    outcomes.Count == 0
                        ? "Downloaded Light Novel release contained no EPUB files."
                        : $"Downloaded Light Novel release contained no usable EPUB: {NovelEpubImportOutcome.Summarize(outcomes)}");
            }

            var workIds = successes
                .Select(outcome => outcome.WorkId!.Value)
                .Distinct()
                .ToArray();
            if (workIds.Length != 1)
            {
                return CompletedDownloadImportResult.RetryLater(
                    "The downloaded package resolved to several Light Novel works and needs owner review.");
            }

            string? metadataWarning = null;
            if (request.Request.Provider.Equals(
                    NovelAniListProvider.ProviderKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await metadata.MatchAsync(
                        workIds[0],
                        request.Request.Provider,
                        request.Request.ExternalId,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    NovelMetadataProviderException or
                    HttpRequestException or
                    TaskCanceledException or
                    System.Text.Json.JsonException)
                {
                    logger.LogWarning(
                        exception,
                        "AniList reconciliation failed after Light Novel request {RequestId} imported.",
                        request.Request.Id);
                    metadataWarning =
                        " Light Novel was imported, but AniList reconciliation needs attention.";
                }
            }

            return CompletedDownloadImportResult.Completed(
                $"{NovelEpubImportOutcome.Summarize(outcomes)}{metadataWarning}",
                $"/Novels/Work/{workIds[0]}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Light Novel import is waiting for storage for request {RequestId}.",
                request.Request.Id);
            return CompletedDownloadImportResult.RetryLater(
                "Light Novel import is waiting for storage.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException)
        {
            logger.LogWarning(
                exception,
                "Downloaded Light Novel release was unsuitable for request {RequestId}.",
                request.Request.Id);
            return CompletedDownloadImportResult.RejectRelease(
                "Downloaded release could not be imported as a Light Novel.");
        }
    }
}

/// <summary>
/// Puts a completed Manga download into the configured Manga library folder with the owner's
/// import mode (shared <see cref="ImportFileTransfer"/>). Only CBZ/ZIP archives and page images
/// are placed; the release keeps its own subfolder below the series folder so two releases never
/// collide. Existing files are never overwritten: an identical file is skipped (idempotent retry),
/// a different one gets a numbered name.
/// </summary>
public sealed class MangaLibraryPlacement(ImportFileTransfer transfer)
{
    private static readonly HashSet<char> InvalidNameCharacters = Path.GetInvalidFileNameChars()
        .Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])
        .ToHashSet();

    /// <summary>
    /// The series folder in the library: the matched series' own folder when it already lives
    /// in the library, otherwise a folder named after the requested title.
    /// </summary>
    public static string SeriesFolder(
        string libraryRoot,
        MangaSeriesLocation? existing,
        string title)
    {
        var root = Path.GetFullPath(libraryRoot);
        if (existing is not null &&
            Directory.Exists(existing.SourcePath) &&
            IsBelow(existing.SourcePath, root))
        {
            return Path.GetFullPath(existing.SourcePath);
        }

        return Path.Combine(root, SafeName(title));
    }

    public static string SafeName(string? value)
    {
        var cleaned = new string((value ?? string.Empty)
                .Select(character => InvalidNameCharacters.Contains(character) || char.IsControl(character) ? '_' : character)
                .ToArray())
            .Trim(' ', '.', '_');
        return cleaned.Length == 0 ? "Manga" : cleaned;
    }

    public static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.Ordinal);

    /// <summary>Returns how many files were placed now (skipped identical files excluded).</summary>
    public int Place(string source, string destination, ImportMode mode)
    {
        if (File.Exists(source))
        {
            if (!MangaImportService.IsImportableFile(source))
            {
                throw new InvalidOperationException("A Manga download file must be CBZ or ZIP.");
            }

            return PlaceFile(source, destination, mode) ? 1 : 0;
        }

        var root = Path.GetFullPath(source);
        var placed = 0;
        foreach (var file in Directory
                     .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(MangaImportService.IsImportableFile)
                     .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                continue;
            }

            if (PlaceFile(file, Path.Combine(destination, relative), mode))
            {
                placed++;
            }
        }

        if (mode == ImportMode.Move)
        {
            DeleteEmptyDirectories(root);
        }

        return placed;
    }

    private bool PlaceFile(string source, string destination, ImportMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            if (new FileInfo(destination).Length == new FileInfo(source).Length)
            {
                if (mode == ImportMode.Move)
                {
                    File.Delete(source);
                }

                return false;
            }

            destination = UniqueName(destination);
        }

        transfer.Transfer(source, destination, mode);
        return true;
    }

    private static string UniqueName(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void DeleteEmptyDirectories(string root)
    {
        try
        {
            foreach (var directory in Directory
                         .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftover empty download folders are harmless.
        }
    }

    private static bool IsBelow(string path, string root)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
               full.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}

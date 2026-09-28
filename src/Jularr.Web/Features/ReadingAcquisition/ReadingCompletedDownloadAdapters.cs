using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Manga;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Novels;

namespace Jularr.Web.Features.ReadingAcquisition;

/// <summary>
/// The Manga importer behind the shared completed-download dispatcher and the Manga inbox scan.
/// With a Manga library folder the release is placed there with the owner's import mode and
/// read from the library; without one it is read in place. A request matched to AniList adds the
/// release to the series already matched to that entry.
/// </summary>
public sealed class MangaCompletedDownloadImportAdapter(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    MediaMappingReviewStore mappingReviewStore,
    ReadingSegmentMappingStore segmentMappings,
    AnimeImportSettingsStore importSettings,
    IHardLinkCreator hardLinks,
    ILogger<MangaCompletedDownloadImportAdapter> logger,
    string? mangaCacheRoot = null)
    : ICompletedDownloadImportAdapter, IMediaInboxImportAdapter
{
    public MediaAcquisitionKind Kind =>
        MediaAcquisitionKind.Manga;

    public async Task<CompletedDownloadImportResult> ImportAsync(
        CompletedDownloadImportRequest request,
        CancellationToken cancellationToken)
    {
        CompletedDownloadPlacement? placement = null;
        try
        {
            var repository = new MangaRepository(db);
            var aniListId = request.Request is { } answered &&
                            answered.Provider.Equals(
                                NovelAniListProvider.ProviderKey,
                                StringComparison.OrdinalIgnoreCase)
                ? answered.ExternalId
                : null;

            // One series per AniList entry: a new volume of a series that is already matched is
            // added to that series instead of creating another one per release (#485 item 7).
            var existing = aniListId is not null
                ? await repository.FindByMetadataAsync(
                    NovelAniListProvider.ProviderKey,
                    aniListId,
                    cancellationToken)
                : null;

            var releaseName = Path.GetFileName(request.SourcePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            var title = request.Request?.Title ??
                        (File.Exists(request.SourcePath)
                            ? Path.GetFileNameWithoutExtension(releaseName)
                            : releaseName);

            var settings = await importSettings.LoadAsync(cancellationToken);
            var library = settings.LibraryFor(MediaAcquisitionKind.Manga);
            var sourceExists = File.Exists(request.SourcePath) || Directory.Exists(request.SourcePath);

            string importSource;
            if (library is null)
            {
                // No Manga library configured: the completed download is read in place.
                placement = new CompletedDownloadPlacement(request.SourcePath, Mode: null);
                if (!sourceExists)
                {
                    return CompletedDownloadImportResult.RetryLater(
                        "The completed Manga files are not currently available.",
                        placement);
                }

                importSource = request.SourcePath;
            }
            else
            {
                var seriesFolder = MangaLibraryPlacement.SeriesFolder(
                    library.LibraryRoot!,
                    existing,
                    title);
                var releaseTarget = Path.Combine(
                    seriesFolder,
                    MangaLibraryPlacement.SafeName(releaseName));
                var mode = settings.ModeFor(MediaAcquisitionKind.Manga);
                placement = new CompletedDownloadPlacement(releaseTarget, mode);

                if (sourceExists)
                {
                    new MangaLibraryPlacement(new ImportFileTransfer(hardLinks)).Place(
                        request.SourcePath,
                        releaseTarget,
                        mode);
                }
                else if (!File.Exists(releaseTarget) && !Directory.Exists(releaseTarget))
                {
                    return CompletedDownloadImportResult.RetryLater(
                        "The completed Manga files are not currently available.",
                        placement);
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
            if (existing is null && aniListId is not null)
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
                        aniListId,
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
                        request.Request?.Id);
                    metadataWarning =
                        " Manga was imported, but AniList reconciliation needs attention.";
                }
            }

            return CompletedDownloadImportResult.Completed(
                $"Imported {imported.ChapterCount} Manga chapter(s).{metadataWarning}",
                $"/Manga/Series/{imported.SeriesId}",
                placement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CrossDeviceLinkException exception)
        {
            logger.LogWarning(
                exception,
                "Manga hardlink import crosses filesystems for '{SourcePath}'.",
                request.SourcePath);
            return CompletedDownloadImportResult.RetryLater(
                "The download and the Manga library are on different filesystems, so a hardlink is impossible. Choose \"Hardlink or copy\", Copy or Move for Manga.",
                placement);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Manga import is waiting for storage for '{SourcePath}'.",
                request.SourcePath);
            return CompletedDownloadImportResult.RetryLater(
                "Manga import is waiting for storage.",
                placement);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException)
        {
            logger.LogWarning(
                exception,
                "Downloaded Manga release '{SourcePath}' was unsuitable.",
                request.SourcePath);
            return CompletedDownloadImportResult.RejectRelease(
                "Downloaded release could not be imported as Manga.",
                placement);
        }
    }

    /// <summary>
    /// Every folder or CBZ/ZIP archive directly in the inbox is one series, named after it, and
    /// goes through the same placement and import as a download.
    /// </summary>
    public async Task<MediaInboxImportResult> ImportInboxAsync(
        string inboxRoot,
        IReadOnlyCollection<string> excludedFolders,
        CancellationToken cancellationToken)
    {
        var entries = Directory
            .EnumerateFileSystemEntries(inboxRoot)
            .Where(entry => Directory.Exists(entry) || MangaImportService.IsImportableFile(entry))
            .Where(entry => !excludedFolders.Any(folder =>
                MangaLibraryPlacement.SamePath(entry, folder) ||
                MediaInboxImportService.IsBelow(folder, entry)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var imported = 0;
        var problems = new List<string>();
        string? lastUrl = null;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ImportAsync(
                new CompletedDownloadImportRequest(null, null, entry, MediaAcquisitionKind.Manga),
                cancellationToken);
            if (result.Disposition == CompletedDownloadImportDisposition.Completed)
            {
                imported++;
                lastUrl = result.ResultUrl;
            }
            else
            {
                problems.Add($"{Path.GetFileName(entry)}: {result.Message}");
            }
        }

        var message = imported == 0 && problems.Count == 0
            ? "No Manga in the inbox."
            : $"Imported {imported} Manga series from the inbox.";
        if (problems.Count > 0)
        {
            message += " " + string.Join(" ", problems);
        }

        return new MediaInboxImportResult(imported, message, imported == 1 ? lastUrl : null);
    }
}

/// <summary>
/// The Light Novel importer behind the shared completed-download dispatcher and the Light Novel
/// inbox scan. EPUBs are read (never moved) and stored in Jularr's Light Novel library.
/// </summary>
public sealed class LightNovelCompletedDownloadImportAdapter(
    NovelEpubImportService importer,
    NovelMetadataService metadata,
    AnimeImportSettingsStore importSettings,
    IHardLinkCreator hardLinks,
    ILogger<LightNovelCompletedDownloadImportAdapter> logger)
    : ICompletedDownloadImportAdapter, IMediaInboxImportAdapter
{
    private static readonly CompletedDownloadPlacement Placement =
        new(NovelVolumeAssetStore.RootPath, ImportMode.Copy);

    public MediaAcquisitionKind Kind =>
        MediaAcquisitionKind.LightNovel;

    public async Task<CompletedDownloadImportResult> ImportAsync(
        CompletedDownloadImportRequest request,
        CancellationToken cancellationToken)
    {
        CompletedDownloadPlacement? placement = null;
        if (!File.Exists(request.SourcePath) &&
            !Directory.Exists(request.SourcePath))
        {
            return CompletedDownloadImportResult.RetryLater(
                "The completed Light Novel files are not currently available.");
        }

        try
        {
            var settings = await importSettings.LoadAsync(cancellationToken);
            var library = settings.LibraryFor(MediaAcquisitionKind.LightNovel);
            var importSource = request.SourcePath;
            if (library is not null)
            {
                var destination = ReadingLibraryPlacement.ReleaseFolder(
                    library.LibraryRoot!,
                    request.Request?.Title ?? Path.GetFileNameWithoutExtension(request.SourcePath));
                var mode = settings.ModeFor(MediaAcquisitionKind.LightNovel);
                placement = new CompletedDownloadPlacement(destination, mode);
                new ReadingLibraryPlacement(new ImportFileTransfer(hardLinks)).PlaceEpubs(
                    request.SourcePath,
                    destination,
                    mode);
                importSource = destination;
            }

            // Recursive, no folder hints, validated before anything is stored (#485 item 8).
            var import = await importer.ImportDownloadAsync(
                importSource,
                cancellationToken,
                recordSourceStoragePath: library is not null);
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
            if (request.Request is { } answered &&
                answered.Provider.Equals(
                    NovelAniListProvider.ProviderKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await metadata.MatchAsync(
                        workIds[0],
                        answered.Provider,
                        answered.ExternalId,
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
                        answered.Id);
                    metadataWarning =
                        " Light Novel was imported, but AniList reconciliation needs attention.";
                }
            }

            return CompletedDownloadImportResult.Completed(
                $"{NovelEpubImportOutcome.Summarize(outcomes)}{metadataWarning}",
                $"/Novels/Work/{workIds[0]}",
                placement ?? Placement);
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
                "Light Novel import is waiting for storage for '{SourcePath}'.",
                request.SourcePath);
            return CompletedDownloadImportResult.RetryLater(
                "Light Novel import is waiting for storage.",
                placement);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException)
        {
            logger.LogWarning(
                exception,
                "Downloaded Light Novel release '{SourcePath}' was unsuitable.",
                request.SourcePath);
            return CompletedDownloadImportResult.RejectRelease(
                "Downloaded release could not be imported as a Light Novel.",
                placement);
        }
    }

    /// <summary>
    /// The inbox keeps its layout: EPUBs directly in the folder resolve their series from
    /// metadata, EPUBs in a subfolder belong to the series named by that folder. Unchanged files
    /// are skipped, so a rescan never duplicates volumes.
    /// </summary>
    public async Task<MediaInboxImportResult> ImportInboxAsync(
        string inboxRoot,
        IReadOnlyCollection<string> excludedFolders,
        CancellationToken cancellationToken)
    {
        var outcomes = await importer.ImportDirectoryAsync(inboxRoot, cancellationToken);
        var works = outcomes
            .Where(outcome => outcome.Succeeded && outcome.WorkId is not null)
            .Select(outcome => outcome.WorkId!.Value)
            .Distinct()
            .ToArray();
        return new MediaInboxImportResult(
            works.Length,
            outcomes.Count == 0
                ? "No Light Novel EPUBs in the inbox."
                : NovelEpubImportOutcome.Summarize(outcomes),
            works.Length == 1 ? $"/Novels/Work/{works[0]}" : null);
    }
}

/// <summary>
/// Places original EPUBs in a durable per-series NAS folder before the reader derives its
/// chapter and asset state. The same transfer policy as Manga is used, but only EPUB files are
/// accepted here; unrelated download artifacts never become library files.
/// </summary>
public sealed class ReadingLibraryPlacement(ImportFileTransfer transfer)
{
    public static string ReleaseFolder(string libraryRoot, string? title) =>
        Path.Combine(Path.GetFullPath(libraryRoot), MangaLibraryPlacement.SafeName(title));

    public void PlaceEpubs(string source, string destination, ImportMode mode)
        => PlaceFiles(
            source,
            destination,
            mode,
            path => path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase),
            "EPUB");

    public void PlaceBookFiles(string source, string destination, ImportMode mode)
        => PlaceFiles(source, destination, mode, BookFileFormats.IsSupported, "Book");

    private void PlaceFiles(
        string source,
        string destination,
        ImportMode mode,
        Func<string, bool> isSupported,
        string mediaName)
    {
        var root = Path.GetFullPath(source);
        var files = File.Exists(root)
            ? [root]
            : Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(isSupported)
                    .ToArray()
                : throw new IOException($"The completed {mediaName} files are not currently available.");

        if (files.Length == 0 || files.Any(file => !isSupported(file)))
        {
            throw new InvalidDataException($"The download contains no supported {mediaName} files.");
        }

        foreach (var file in files)
        {
            var relative = File.Exists(root) ? Path.GetFileName(file) : Path.GetRelativePath(root, file);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new InvalidDataException("The EPUB path is outside the completed download.");
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                if (new FileInfo(target).Length == new FileInfo(file).Length)
                {
                    if (mode == ImportMode.Move && !MangaLibraryPlacement.SamePath(file, target))
                    {
                        File.Delete(file);
                    }

                    continue;
                }

                target = UniqueName(target);
            }

            transfer.Transfer(file, target, mode);
        }
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

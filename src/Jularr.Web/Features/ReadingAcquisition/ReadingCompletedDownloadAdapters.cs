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
    ReadingSegmentMappingStore segmentMappings)
    : ICompletedDownloadImportAdapter
{
    public MediaAcquisitionKind Kind =>
        MediaAcquisitionKind.Manga;

    public async Task<CompletedDownloadImportResult> ImportAsync(
        CompletedDownloadImportRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.SourcePath) &&
            !Directory.Exists(request.SourcePath))
        {
            return CompletedDownloadImportResult.RetryLater(
                $"The completed Manga path is not currently available: {request.SourcePath}");
        }

        try
        {
            var repository = new MangaRepository(db);
            var importer = new MangaImportService(repository);
            var imported = await importer.ImportAsync(
                request.SourcePath,
                cancellationToken);

            string? metadataWarning = null;
            if (request.Request.Provider.Equals(
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
                    metadataWarning =
                        $" Manga was imported, but AniList reconciliation needs attention: {exception.Message}";
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
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            return CompletedDownloadImportResult.RetryLater(
                $"Manga import is waiting for storage: {exception.Message}");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException)
        {
            return CompletedDownloadImportResult.RejectRelease(
                $"Downloaded release could not be imported as Manga: {exception.Message}");
        }
    }
}

public sealed class LightNovelCompletedDownloadImportAdapter(
    NovelEpubImportService importer,
    NovelMetadataService metadata)
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
                $"The completed Light Novel path is not currently available: {request.SourcePath}");
        }

        try
        {
            IReadOnlyList<NovelEpubImportOutcome> outcomes;
            if (File.Exists(request.SourcePath))
            {
                if (!Path.GetExtension(request.SourcePath)
                    .Equals(".epub", StringComparison.OrdinalIgnoreCase))
                {
                    return CompletedDownloadImportResult.RejectRelease(
                        "Downloaded Light Novel release did not contain an importable EPUB.");
                }

                await using var stream = new FileStream(
                    request.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 81920,
                    useAsync: true);
                outcomes = await importer.ImportUploadsAsync(
                    [(stream, Path.GetFileName(request.SourcePath))],
                    targetWorkId: null,
                    cancellationToken);
            }
            else
            {
                outcomes = await importer.ImportDirectoryAsync(
                    request.SourcePath,
                    cancellationToken);
            }

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
                    metadataWarning =
                        $" Light Novel was imported, but AniList reconciliation needs attention: {exception.Message}";
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
            return CompletedDownloadImportResult.RetryLater(
                $"Light Novel import is waiting for storage: {exception.Message}");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException)
        {
            return CompletedDownloadImportResult.RejectRelease(
                $"Downloaded release could not be imported as a Light Novel: {exception.Message}");
        }
    }
}

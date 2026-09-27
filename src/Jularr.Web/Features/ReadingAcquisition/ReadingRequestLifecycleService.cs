using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Manga;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Operations;

namespace Jularr.Web.Features.ReadingAcquisition;

/// <summary>
/// Keeps Manga and Light-Novel requests moving after the initial Add/Request action.
///
/// SABnzbdOperationMonitorService remains the single component that projects the external
/// download state onto Operations. This service consumes that persisted state only:
/// - due Approved requests are searched again,
/// - failed downloads continue with the next untried release,
/// - completed downloads are imported through the existing Manga/Novel importers,
/// - successful imports close the acquisition request.
/// </summary>
public sealed class ReadingRequestLifecycleService(
    IServiceScopeFactory scopes,
    ILogger<ReadingRequestLifecycleService> logger,
    TimeProvider clock) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await ProcessOnceAsync(
                    scope.ServiceProvider,
                    clock.GetUtcNow().UtcDateTime,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not advance Manga/Light-Novel acquisition requests.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public static async Task<int> ProcessOnceAsync(
        IServiceProvider services,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var advanced = 0;
        foreach (var kind in new[]
                 {
                     MediaAcquisitionKind.Manga,
                     MediaAcquisitionKind.LightNovel
                 })
        {
            advanced += await RecoverDownloadsAsync(
                services,
                kind,
                cancellationToken);
            advanced += await SearchDueAsync(
                services,
                kind,
                nowUtc,
                cancellationToken);
        }

        return advanced;
    }

    public static async Task<int> SearchDueAsync(
        IServiceProvider services,
        MediaAcquisitionKind kind,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var requestService = services.GetRequiredService<AcquisitionRequestService>();
        var due = new List<AcquisitionRequest>();

        foreach (var request in await store.ListByStatusAsync(
                     kind,
                     AcquisitionRequestStatus.Approved,
                     cancellationToken))
        {
            var payload = Payload(request);
            if (payload.NextSearchUtc is null ||
                payload.NextSearchUtc <= nowUtc)
            {
                due.Add(request);
            }
        }

        foreach (var request in due)
        {
            await requestService.ContinueAsync(
                request.Id,
                cancellationToken);
        }

        return due.Count;
    }

    public static async Task<int> RecoverDownloadsAsync(
        IServiceProvider services,
        MediaAcquisitionKind kind,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var operations = new OperationStore(
            services.GetRequiredService<AppDbContext>());
        var advanced = 0;

        foreach (var request in await store.ListDownloadingAsync(
                     kind,
                     cancellationToken))
        {
            var operation = request.OperationId is { } operationId
                ? await operations.GetAsync(
                    operationId,
                    cancellationToken)
                : null;

            if (operation is null)
            {
                await ContinueWithProblemAsync(
                    services,
                    request,
                    "The download operation no longer exists.",
                    cancellationToken);
                advanced++;
                continue;
            }

            if (operation.Status is
                OperationStatus.Failed or
                OperationStatus.Cancelled or
                OperationStatus.Interrupted)
            {
                var problem = string.IsNullOrWhiteSpace(operation.Error)
                    ? "The download failed."
                    : $"The download failed: {operation.Error.Trim().TrimEnd('.')}.";
                await ContinueWithProblemAsync(
                    services,
                    request,
                    problem,
                    cancellationToken);
                advanced++;
                continue;
            }

            if (operation.Status != OperationStatus.Succeeded)
            {
                continue;
            }

            var storagePath = await ResolveStoragePathAsync(
                services,
                operation,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                // SABnzbd can briefly expose a completed operation before its history entry
                // carries the final path. Leave the request Downloading and retry next pass.
                continue;
            }

            var imported = kind switch
            {
                MediaAcquisitionKind.Manga => await ImportMangaAsync(
                    services,
                    request,
                    storagePath,
                    cancellationToken),
                MediaAcquisitionKind.LightNovel => await ImportLightNovelAsync(
                    services,
                    request,
                    storagePath,
                    cancellationToken),
                _ => new ImportResult(false, null, "Unsupported reading media type.")
            };

            if (!imported.Success)
            {
                await ContinueWithProblemAsync(
                    services,
                    request,
                    imported.Message,
                    cancellationToken);
                advanced++;
                continue;
            }

            await store.UpdateStatusAsync(
                request.Id,
                AcquisitionRequestStatus.Completed,
                imported.Message,
                operation.Id,
                imported.ResultUrl,
                decidedByProfileId: null,
                cancellationToken);
            advanced++;
        }

        return advanced;
    }

    private static async Task<ImportResult> ImportMangaAsync(
        IServiceProvider services,
        AcquisitionRequest request,
        string storagePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var repository = new MangaRepository(db);
            var importer = new MangaImportService(repository);
            var result = await importer.ImportAsync(
                storagePath,
                cancellationToken);

            string? metadataWarning = null;
            if (request.Provider.Equals(
                    NovelAniListProvider.ProviderKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var metadata = new MangaAniListService(
                        repository,
                        services.GetRequiredService<IHttpClientFactory>(),
                        services.GetRequiredService<MediaMappingReviewStore>(),
                        services.GetRequiredService<ReadingSegmentMappingStore>());
                    await metadata.MatchAsync(
                        result.SeriesId,
                        request.ExternalId,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    HttpRequestException or
                    TaskCanceledException)
                {
                    metadataWarning =
                        $" Manga was imported, but AniList matching needs attention: {exception.Message}";
                }
            }

            return new ImportResult(
                true,
                $"/Manga/Series/{result.SeriesId}",
                $"Imported {result.ChapterCount} Manga chapter(s).{metadataWarning}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            return new ImportResult(
                false,
                null,
                $"Downloaded release could not be imported as Manga: {exception.Message}");
        }
    }

    private static async Task<ImportResult> ImportLightNovelAsync(
        IServiceProvider services,
        AcquisitionRequest request,
        string storagePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var importer = services.GetRequiredService<NovelEpubImportService>();
            IReadOnlyList<NovelEpubImportOutcome> outcomes;

            if (File.Exists(storagePath) &&
                Path.GetExtension(storagePath)
                    .Equals(".epub", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = new FileStream(
                    storagePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    81920,
                    useAsync: true);
                outcomes = await importer.ImportUploadsAsync(
                    [(stream, Path.GetFileName(storagePath))],
                    targetWorkId: null,
                    cancellationToken);
            }
            else
            {
                outcomes = await importer.ImportDirectoryAsync(
                    storagePath,
                    cancellationToken);
            }

            var successes = outcomes
                .Where(outcome => outcome.Succeeded &&
                                  outcome.WorkId is not null)
                .ToArray();
            if (successes.Length == 0)
            {
                return new ImportResult(
                    false,
                    null,
                    outcomes.Count == 0
                        ? "Downloaded release contained no EPUB files."
                        : $"Downloaded release contained no usable EPUB: {NovelEpubImportOutcome.Summarize(outcomes)}");
            }

            var workIds = successes
                .Select(outcome => outcome.WorkId!.Value)
                .Distinct()
                .ToArray();
            if (workIds.Length != 1)
            {
                return new ImportResult(
                    false,
                    null,
                    "Downloaded release resolved to more than one Light Novel series.");
            }

            string? metadataWarning = null;
            if (request.Provider.Equals(
                    NovelAniListProvider.ProviderKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await services.GetRequiredService<NovelMetadataService>()
                        .MatchAsync(
                            workIds[0],
                            request.Provider,
                            request.ExternalId,
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
                    TaskCanceledException)
                {
                    metadataWarning =
                        $" Light Novel was imported, but AniList matching needs attention: {exception.Message}";
                }
            }

            return new ImportResult(
                true,
                $"/Novels/Work/{workIds[0]}",
                $"{NovelEpubImportOutcome.Summarize(outcomes)}{metadataWarning}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            return new ImportResult(
                false,
                null,
                $"Downloaded release could not be imported as a Light Novel: {exception.Message}");
        }
    }

    private static async Task ContinueWithProblemAsync(
        IServiceProvider services,
        AcquisitionRequest request,
        string problem,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var payload = Payload(request) with
        {
            LastProblem = problem,
            NextSearchUtc = null
        };
        await store.UpdatePayloadAsync(
            request.Id,
            System.Text.Json.JsonSerializer.Serialize(
                payload,
                System.Text.Json.JsonSerializerOptions.Web),
            cancellationToken);

        await services.GetRequiredService<AcquisitionRequestService>()
            .ContinueAsync(
                request.Id,
                cancellationToken);
    }

    private static ReadingRequestPayload Payload(
        AcquisitionRequest request)
    {
        var fallback = request.Kind == MediaAcquisitionKind.Manga
            ? new ReadingAcquisitionTarget(
                request.Kind,
                request.Title,
                string.IsNullOrWhiteSpace(request.Subtitle)
                    ? []
                    : [request.Subtitle])
            : new ReadingAcquisitionTarget(
                request.Kind,
                request.Title,
                [],
                request.Subtitle);

        return ReadingAcquisitionEngine.ReadPayload(
            request,
            fallback);
    }

    private static async Task<string?> ResolveStoragePathAsync(
        IServiceProvider services,
        OperationSnapshot operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation.ExternalId))
        {
            return null;
        }

        var entries = await services
            .GetRequiredService<DownloadClientStore>()
            .LoadAllAsync(cancellationToken);

        DownloadClientEntry? entry = null;
        if (DownloadOperationDetails.TryParse(
                operation.Details,
                out var details) &&
            details is not null)
        {
            entry = entries.FirstOrDefault(
                candidate => candidate.Id == details.ClientEntryId);
        }

        entry ??= entries
            .Where(candidate =>
                candidate.Type == DownloadClientType.Sabnzbd &&
                candidate.Enabled)
            .OrderBy(candidate => candidate.Priority)
            .FirstOrDefault();

        if (entry is null)
        {
            return null;
        }

        try
        {
            var statuses = await services
                .GetRequiredService<IDownloadClient>()
                .GetStatusAsync(
                    entry,
                    [operation.ExternalId],
                    cancellationToken);
            return statuses
                .FirstOrDefault(status =>
                    status.ExternalId == operation.ExternalId &&
                    status.IsCompleted)?
                .StoragePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            DownloadClientException or
            InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record ImportResult(
        bool Success,
        string? ResultUrl,
        string Message);
}

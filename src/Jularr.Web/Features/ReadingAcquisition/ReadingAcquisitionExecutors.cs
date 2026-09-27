using System.Text.Json;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Novels;

namespace Jularr.Web.Features.ReadingAcquisition;

public sealed record ReadingRequestPayload(
    string Title,
    IReadOnlyList<string> Aliases,
    string? Author,
    int? RequestedVolume = null,
    double? RequestedChapterStart = null,
    double? RequestedChapterEnd = null,
    IReadOnlyList<string>? PreferredLanguages = null,
    IReadOnlyList<string>? TriedReleaseIds = null,
    int Searches = 0,
    DateTime? NextSearchUtc = null,
    string? LastProblem = null);

public sealed class ReadingAcquisitionEngine(
    IndexerSearchCoordinator indexers,
    DownloadClientStore downloadClients,
    DownloadClientSubmissionService downloads,
    AcquisitionAccessStore requests,
    TimeProvider clock)
{
    public const string OperationKind = "reading-usenet-download";
    public const int MaxSearches = 12;

    public static TimeSpan SearchBackoff(int searches) =>
        TimeSpan.FromHours(searches switch
        {
            <= 1 => 6,
            2 => 12,
            _ => 24
        });

    public async Task<AcquisitionExecution> ExecuteAsync(
        AcquisitionRequest request,
        ReadingAcquisitionTarget initialTarget,
        CancellationToken cancellationToken)
    {
        if (request.Kind is not (MediaAcquisitionKind.Manga or MediaAcquisitionKind.LightNovel))
        {
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Failed,
                "Reading acquisition only supports Manga and Light Novels.");
        }

        var payload = ReadPayload(request, initialTarget);
        var target = ToTarget(request.Kind, payload);

        if (!await indexers.HasEnabledIndexerAsync(cancellationToken))
        {
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Failed,
                "No Usenet indexer is configured.");
        }

        if (!(await downloadClients.LoadAllAsync(cancellationToken))
            .Any(entry => entry.Enabled && entry.Type == DownloadClientType.Sabnzbd))
        {
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Failed,
                "SABnzbd is not configured.");
        }

        var search = await ReadingUsenetSearch.SearchAsync(
            indexers,
            target,
            cancellationToken);
        var searches = payload.Searches + 1;
        var tried = new HashSet<string>(
            payload.TriedReleaseIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        var ranked = search.Ranked
            .FirstOrDefault(candidate =>
                candidate.Score > 0 &&
                !tried.Contains(candidate.Release.Identity));

        if (ranked?.Release.InternalDownloadUri is not { } downloadUri)
        {
            var reason = search.Ranked.Any(candidate =>
                    candidate.Score > 0 &&
                    tried.Contains(candidate.Release.Identity))
                ? "Every matching release was tried already."
                : search.FailureMessage;

            if (searches >= MaxSearches)
            {
                await SavePayloadAsync(
                    request,
                    payload with
                    {
                        Searches = searches,
                        NextSearchUtc = null,
                        LastProblem = reason
                    },
                    cancellationToken);
                return new AcquisitionExecution(
                    AcquisitionRequestStatus.Failed,
                    WithProblem(payload, $"{reason} Gave up after {searches} searches."));
            }

            var next = clock.GetUtcNow().UtcDateTime + SearchBackoff(searches);
            await SavePayloadAsync(
                request,
                payload with
                {
                    Searches = searches,
                    NextSearchUtc = next,
                    LastProblem = reason
                },
                cancellationToken);
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Approved,
                WithProblem(payload, $"{reason} Searching again {next:yyyy-MM-dd HH:mm} UTC."));
        }

        tried.Add(ranked.Release.Identity);
        await SavePayloadAsync(
            request,
            payload with
            {
                TriedReleaseIds = tried.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                Searches = searches,
                NextSearchUtc = null,
                LastProblem = null
            },
            cancellationToken);

        var outcome = await downloads.SubmitAsync(
            new DownloadSubmissionSpec(
                OperationKind,
                request.Kind == MediaAcquisitionKind.Manga
                    ? "Download Manga"
                    : "Download Light Novel",
                payload.Title,
                request.RequestedByProfileId,
                downloadUri,
                ranked.Release.Title,
                request.Kind),
            cancellationToken);

        if (outcome.Accepted)
        {
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Downloading,
                payload.LastProblem is null
                    ? ranked.Release.Title
                    : $"{payload.LastProblem} Trying {ranked.Release.Title}.",
                outcome.OperationId);
        }

        var retryProblem = outcome.Message;
        if (searches >= MaxSearches)
        {
            await SavePayloadAsync(
                request,
                payload with
                {
                    TriedReleaseIds = tried.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                    Searches = searches,
                    NextSearchUtc = null,
                    LastProblem = retryProblem
                },
                cancellationToken);
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Failed,
                retryProblem,
                outcome.OperationId);
        }

        var retryAt = clock.GetUtcNow().UtcDateTime + SearchBackoff(searches);
        await SavePayloadAsync(
            request,
            payload with
            {
                TriedReleaseIds = tried.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                Searches = searches,
                NextSearchUtc = retryAt,
                LastProblem = retryProblem
            },
            cancellationToken);
        return new AcquisitionExecution(
            AcquisitionRequestStatus.Approved,
            $"{retryProblem} Searching again {retryAt:yyyy-MM-dd HH:mm} UTC.",
            outcome.OperationId);
    }

    public static ReadingRequestPayload ReadPayload(
        AcquisitionRequest request,
        ReadingAcquisitionTarget fallback)
    {
        if (!string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            try
            {
                var persisted = JsonSerializer.Deserialize<ReadingRequestPayload>(
                    request.PayloadJson,
                    JsonSerializerOptions.Web);
                if (persisted is not null &&
                    !string.IsNullOrWhiteSpace(persisted.Title))
                {
                    return persisted;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new ReadingRequestPayload(
            fallback.Title,
            fallback.Aliases,
            fallback.Author,
            fallback.RequestedVolume,
            fallback.RequestedChapterStart,
            fallback.RequestedChapterEnd,
            fallback.PreferredLanguages);
    }

    public static ReadingAcquisitionTarget ToTarget(
        MediaAcquisitionKind kind,
        ReadingRequestPayload payload) =>
        new(
            kind,
            payload.Title,
            payload.Aliases ?? [],
            payload.Author,
            payload.RequestedVolume,
            payload.RequestedChapterStart,
            payload.RequestedChapterEnd,
            payload.PreferredLanguages);

    private Task SavePayloadAsync(
        AcquisitionRequest request,
        ReadingRequestPayload payload,
        CancellationToken cancellationToken) =>
        requests.UpdatePayloadAsync(
            request.Id,
            JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
            cancellationToken);

    private static string WithProblem(
        ReadingRequestPayload payload,
        string message) =>
        payload.LastProblem is null
            ? message
            : $"{payload.LastProblem} {message}";
}

public sealed class MangaAcquisitionRequestExecutor(
    ReadingAcquisitionEngine engine) : IAcquisitionRequestExecutor
{
    public MediaAcquisitionKind Kind => MediaAcquisitionKind.Manga;

    public Task<AcquisitionExecution> ExecuteAsync(
        AcquisitionRequest request,
        CancellationToken cancellationToken)
    {
        var aliases = string.IsNullOrWhiteSpace(request.Subtitle)
            ? Array.Empty<string>()
            : new[] { request.Subtitle.Trim() };

        return engine.ExecuteAsync(
            request,
            new ReadingAcquisitionTarget(
                MediaAcquisitionKind.Manga,
                request.Title,
                aliases),
            cancellationToken);
    }
}

public sealed class LightNovelAcquisitionRequestExecutor(
    ReadingAcquisitionEngine engine,
    NovelAniListProvider aniList) : IAcquisitionRequestExecutor
{
    public MediaAcquisitionKind Kind => MediaAcquisitionKind.LightNovel;

    public async Task<AcquisitionExecution> ExecuteAsync(
        AcquisitionRequest request,
        CancellationToken cancellationToken)
    {
        var aliases = new List<string>();
        var canonicalTitle = request.Title;

        if (request.Provider.Equals(
                NovelAniListProvider.ProviderKey,
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var candidate = await aniList.GetAsync(
                    request.ExternalId,
                    cancellationToken);
                if (candidate is not null)
                {
                    canonicalTitle = candidate.PreferredTitle;
                    if (!string.IsNullOrWhiteSpace(candidate.NativeTitle))
                    {
                        aliases.Add(candidate.NativeTitle);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is NovelMetadataProviderException or
                HttpRequestException or
                TaskCanceledException)
            {
                // Catalog metadata is enrichment only. The request title still gives the
                // indexer search a stable canonical query.
            }
        }

        if (!canonicalTitle.Equals(request.Title, StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(request.Title);
        }

        return await engine.ExecuteAsync(
            request,
            new ReadingAcquisitionTarget(
                MediaAcquisitionKind.LightNovel,
                canonicalTitle,
                aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                request.Subtitle),
            cancellationToken);
    }
}

using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.ReadingDiscovery;
using Jularr.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Novels;

public sealed record NovelCatalogResult(
    ReadingCatalogCandidate Item,
    string? LocalUrl,
    string? RequestStatus);

[NovelEpubUploadRequestLimits("UploadEpub")]
public sealed class IndexModel(
    NovelCatalogQueries catalog,
    NovelImportService imports,
    NovelEpubImportService epubImports,
    BookCatalogService books,
    NovelAniListProvider readingProvider,
    IHttpClientFactory httpClientFactory,
    AcquisitionRequestService requests,
    AcquisitionAccessStore requestStore,
    CurrentAccountContext account,
    OperationRunner operations,
    AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<NovelListItem> Works { get; private set; } = [];
    public IReadOnlyList<NovelListItem> ContinueReading { get; private set; } = [];
    public IReadOnlyList<NovelCatalogResult> SearchResults { get; private set; } = [];
    public AcquisitionCapabilities Access { get; private set; } =
        AcquisitionCapabilities.Resolve(
            AcquisitionAccessPolicy.Default(MediaAcquisitionKind.LightNovel),
            false);
    public string SearchQuery { get; private set; } = "";
    public bool IsOwner => account.IsOwner;
    public bool IsInboxConfigured => books.IsInboxConfigured;

    public async Task OnGetAsync(
        string? q,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Works = await catalog.GetLibraryAsync(account.ProfileId, cancellationToken);
        ContinueReading = Works
            .Where(x => x.HasProgress)
            .OrderByDescending(x => x.LastReadAt)
            .Take(8)
            .ToArray();

        Access = await requests.GetCapabilitiesAsync(
            MediaAcquisitionKind.LightNovel,
            cancellationToken);
        SearchQuery = ReadingCatalogSearch.NormalizeQuery(q);

        if (SearchQuery.Length == 0)
        {
            return;
        }

        var syosetuClient = httpClientFactory.CreateClient();
        var candidates = await ReadingCatalogSearch.SearchLightNovelsAsync(
            readingProvider,
            syosetuClient,
            SearchQuery,
            18,
            cancellationToken);

        var aniListIds = candidates
            .Where(candidate =>
                candidate.Provider.Equals(
                    NovelAniListProvider.ProviderKey,
                    StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.ExternalId)
            .ToArray();
        var syosetuIds = candidates
            .Where(candidate =>
                candidate.Provider.Equals(
                    NcodeNovelSourceProvider.ProviderKey,
                    StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.ExternalId)
            .ToArray();

        var localWorks = await db.NovelWorks
            .AsNoTracking()
            .Where(work =>
                (work.MetadataProvider == NovelAniListProvider.ProviderKey &&
                 work.MetadataExternalId != null &&
                 aniListIds.Contains(work.MetadataExternalId))
                ||
                (work.SourceProvider == NcodeNovelSourceProvider.ProviderKey &&
                 syosetuIds.Contains(work.SourceKey)))
            .Select(work => new
            {
                work.Id,
                work.MetadataProvider,
                work.MetadataExternalId,
                work.SourceProvider,
                work.SourceKey
            })
            .ToListAsync(cancellationToken);

        var local = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var work in localWorks)
        {
            if (work.MetadataProvider == NovelAniListProvider.ProviderKey &&
                work.MetadataExternalId is { Length: > 0 } metadataId)
            {
                local[$"{NovelAniListProvider.ProviderKey}:{metadataId}"] =
                    $"/Novels/Work/{work.Id}";
            }

            if (work.SourceProvider == NcodeNovelSourceProvider.ProviderKey &&
                work.SourceKey.Length > 0)
            {
                local[$"{NcodeNovelSourceProvider.ProviderKey}:{work.SourceKey}"] =
                    $"/Novels/Work/{work.Id}";
            }
        }

        var openRequests = (await requestStore.ListAsync(
                MediaAcquisitionKind.LightNovel,
                requestedByProfileId: null,
                openOnly: true,
                limit: 500,
                cancellationToken))
            .GroupBy(
                request => $"{request.Provider}:{request.ExternalId}",
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => AcquisitionAccessNames.Status(group.First().Status),
                StringComparer.Ordinal);

        SearchResults = candidates
            .Select(candidate =>
            {
                local.TryGetValue(
                    candidate.Identity,
                    out var localUrl);
                openRequests.TryGetValue(
                    candidate.Identity,
                    out var requestStatus);

                return new NovelCatalogResult(
                    candidate,
                    localUrl,
                    requestStatus);
            })
            .ToArray();
    }

    public async Task<IActionResult> OnPostAddCatalogAsync(
        string? provider,
        string? externalId,
        string? title,
        string? nativeTitle,
        string? author,
        string? coverImageUrl,
        string? q,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = provider?.Trim().ToLowerInvariant();
        var normalizedId = externalId?.Trim();
        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(normalizedId) ||
            normalizedProvider is not (
                NovelAniListProvider.ProviderKey or
                NcodeNovelSourceProvider.ProviderKey))
        {
            return BadRequest();
        }

        if (normalizedProvider == NovelAniListProvider.ProviderKey &&
            (!int.TryParse(normalizedId, out var aniListId) ||
             aniListId <= 0))
        {
            return BadRequest();
        }

        if (normalizedProvider == NcodeNovelSourceProvider.ProviderKey &&
            !SyosetuCatalogClient.IsValidNcode(normalizedId))
        {
            return BadRequest();
        }

        var capabilities = await requests.GetCapabilitiesAsync(
            MediaAcquisitionKind.LightNovel,
            cancellationToken);
        if (!capabilities.CanAdd)
        {
            return Forbid();
        }

        if (normalizedProvider == NcodeNovelSourceProvider.ProviderKey &&
            !capabilities.AddCreatesRequest)
        {
            var sourceUrl =
                $"https://ncode.syosetu.com/{normalizedId.ToLowerInvariant()}/";

            try
            {
                var workId = await operations.RunAsync(
                    new OperationDescriptor(
                        "novel-catalog-import",
                        "Novels",
                        "Import public novel source",
                        title.Trim(),
                        account.ProfileId,
                        OperationLane.Normal,
                        IsDownload: true,
                        Retryable: false),
                    async (operation, token) =>
                    {
                        await operation.ReportAsync(
                            10,
                            "Importing public novel source.",
                            cancellationToken: token);

                        return await imports.ImportWorkAsync(
                            sourceUrl,
                            token);
                    },
                    "Public novel source imported.",
                    cancellationToken);

                return RedirectToPage(
                    "/Novels/Work",
                    new { id = workId });
            }
            catch (InvalidOperationException exception)
            {
                TempData["Status"] = exception.Message;
                return RedirectToPage(
                    new
                    {
                        q = ReadingCatalogSearch.NormalizeQuery(q)
                    });
            }
        }

        try
        {
            var request = await requests.SubmitAsync(
                new AcquisitionRequestDraft(
                    MediaAcquisitionKind.LightNovel,
                    normalizedProvider,
                    normalizedId,
                    title.Trim(),
                    string.IsNullOrWhiteSpace(author)
                        ? string.IsNullOrWhiteSpace(nativeTitle)
                            ? null
                            : nativeTitle.Trim()
                        : author.Trim(),
                    string.IsNullOrWhiteSpace(coverImageUrl)
                        ? null
                        : coverImageUrl.Trim()),
                cancellationToken);

            if (request.Status == AcquisitionRequestStatus.Completed &&
                request.ResultUrl is { Length: > 0 } resultUrl &&
                resultUrl.StartsWith('/', StringComparison.Ordinal))
            {
                return LocalRedirect(resultUrl);
            }

            var ui = await UiRequestLocalization.GetBundleAsync(
                HttpContext,
                db);
            TempData["Status"] = request.StatusMessage
                ?? ui[
                    "requests.status."
                    + AcquisitionAccessNames.Status(request.Status)];

            return RedirectToPage(
                new
                {
                    q = ReadingCatalogSearch.NormalizeQuery(q)
                });
        }
        catch (AcquisitionAccessDeniedException)
        {
            return Forbid();
        }
    }

    public async Task<IActionResult> OnPostImportAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var store = new OperationStore(db);
        var operationId = await store.CreateAsync(
            new OperationDescriptor(
                "novel-import",
                "Novels",
                "Import novel source",
                ProfileId: account.ProfileId,
                Lane: OperationLane.Normal,
                IsDownload: true,
                Retryable: false),
            cancellationToken);

        await store.MarkRunningAsync(operationId, cancellationToken);

        try
        {
            var workId = await imports.ImportWorkAsync(sourceUrl, cancellationToken);
            await store.MarkSucceededAsync(
                operationId,
                "Novel metadata and chapter index imported.",
                cancellationToken);

            TempData["Status"] = ui["discover.import.novelImported"];
            return RedirectToPage("/Novels/Work", new { id = workId });
        }
        catch (InvalidOperationException exception)
        {
            await store.MarkFailedAsync(
                operationId,
                $"{exception.GetType().Name}: {exception.Message}",
                CancellationToken.None);
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostUploadEpubAsync(
        List<IFormFile>? epubs,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var outcomes = await NovelEpubUploads.ImportAsync(
            epubImports,
            operations,
            account.ProfileId,
            epubs,
            targetWorkId: null,
            cancellationToken);

        if (outcomes is null)
        {
            var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
            TempData["Status"] = ui.Format(
                "novels.index.chooseEpubFiles",
                ("max", NovelEpubUploadRequestLimitsAttribute.MaximumFiles));
            return RedirectToPage();
        }

        TempData["Status"] = NovelEpubImportOutcome.Summarize(outcomes);
        var series = outcomes
            .Where(x => x.Succeeded && x.WorkId is not null)
            .Select(x => x.WorkId!.Value)
            .Distinct()
            .ToArray();

        return series.Length == 1
            ? RedirectToPage("/Novels/Work", new { id = series[0] })
            : RedirectToPage();
    }

    public async Task<IActionResult> OnPostScanInboxAsync(
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var inbox = books.InboxPath;
        if (inbox is null)
        {
            var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
            TempData["Status"] = ui["novels.index.configureInboxFirst"];
            return RedirectToPage();
        }

        try
        {
            var outcomes = await operations.RunAsync(
                new OperationDescriptor(
                    "novel-epub-inbox-import",
                    "Novels",
                    "Import light-novel inbox",
                    ProfileId: account.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        "Scanning light-novel inbox.",
                        cancellationToken: token);
                    return await epubImports.ImportInboxAsync(inbox, token);
                },
                "Light-novel inbox scan completed.",
                cancellationToken);

            TempData["Status"] = NovelEpubImportOutcome.Summarize(outcomes);
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }
}

using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Manga;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Tracking;
using Jularr.Web.Features.Watchlist;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Manga;

public sealed class SeriesModel(
    AppDbContext db,
    CurrentAccountContext account,
    IHttpClientFactory httpClientFactory,
    OperationRunner operations,
    MediaMappingReviewStore mappingReviewStore,
    AniListAccountService aniListAccount,
    FranchiseStore franchises,
    FranchiseService franchiseService) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public MangaSeriesDetail Series { get; private set; } = null!;
    public MangaProgressItem? Progress { get; private set; }
    public ExternalProgressSummary? ExternalProgress { get; private set; }
    public IReadOnlyList<MangaAniListCandidate> SearchResults { get; private set; } = [];
    public string Query { get; private set; } = "";
    public string? SearchError { get; private set; }
    public bool IsOwner => account.IsOwner;
    public IReadOnlyList<FranchiseSummary> Franchises { get; private set; } = [];
    public IReadOnlyList<FranchiseRelationGroup> FranchiseGroups { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? q,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var repository = new MangaRepository(db);
        var series = await repository.GetSeriesAsync(id, cancellationToken);
        if (series is null)
        {
            return NotFound();
        }

        Series = series;
        var franchiseSource = await repository.GetAutoMatchSourceAsync(id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(franchiseSource?.MetadataExternalId))
        {
            var identity = new WatchlistIdentity(
                WatchlistMediaType.Manga,
                "anilist",
                franchiseSource.MetadataExternalId);
            Franchises = await franchises.FindForMemberAsync(identity, cancellationToken);
            FranchiseGroups = await franchiseService.GetRelationGroupsAsync(identity, cancellationToken);
        }

        Progress = await repository.GetProgressAsync(
            account.ProfileId,
            id,
            cancellationToken);

        Query = q?.Trim() ?? "";
        if (account.IsOwner && Query.Length > 0)
        {
            var metadata = new MangaAniListService(
                    repository,
                    httpClientFactory,
                    mappingReviewStore);
            try
            {
                SearchResults = await metadata.SearchAsync(
                    Query,
                    cancellationToken);
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested &&
                exception is InvalidOperationException
                    or HttpRequestException
                    or JsonException
                    or TaskCanceledException)
            {
                // The explicit search may fail; the local series page must not.
                SearchError = exception is TaskCanceledException
                    ? Ui["manga.series.searchTimeout"]
                    : Ui.Format("manga.series.searchUnavailable", ("reason", exception.Message));
            }
        }

        // Local-only: remote AniList progress is loaded after first paint
        // through OnGetExternalProgressAsync.
        ExternalProgress = await aniListAccount.GetMangaProgressSummaryAsync(
            id,
            cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnGetExternalProgressAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var source = await new MangaRepository(db).GetAutoMatchSourceAsync(
            id,
            cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        var state = await aniListAccount.GetMangaProgressStateAsync(
            id,
            cancellationToken);
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        Response.Headers.CacheControl = "no-store";
        return Partial(
            "_ExternalProgressState",
            new ExternalProgressRemoteView(
                ExternalProgressMediaKind.Manga,
                state,
                "SyncAniList",
                ui));
    }

    public async Task<IActionResult> OnPostSyncAniListAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await operations.RunAsync(
            new OperationDescriptor(
                "anilist-manga-progress-sync",
                "AniList",
                "Sync Manga progress",
                ProfileId: account.ProfileId,
                Lane: OperationLane.Normal,
                Retryable: false),
            (_, token) => aniListAccount.SyncMangaProgressAsync(
                id,
                token),
            "Manga progress sync completed.",
            cancellationToken);

        TempData["Status"] = result.Message;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMatchAsync(
        Guid id,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        await operations.RunAsync(
            new OperationDescriptor(
                "manga-anilist-match",
                "Manga",
                "Match Manga metadata",
                ProfileId: account.ProfileId,
                Lane: OperationLane.Normal,
                Retryable: false),
            async (_, token) =>
            {
                var repository = new MangaRepository(db);
                var metadata = new MangaAniListService(
                    repository,
                    httpClientFactory,
                    mappingReviewStore);
                await metadata.MatchAsync(id, externalId, token);
            },
            "Manga metadata matched.",
            cancellationToken);

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var repository = new MangaRepository(db);
        var series = await repository.GetSeriesAsync(id, cancellationToken);
        if (series is null)
        {
            return NotFound();
        }

        var result = await operations.RunAsync(
            new OperationDescriptor(
                "manga-refresh",
                "Manga",
                "Refresh Manga source",
                series.Title,
                account.ProfileId,
                OperationLane.Normal,
                Retryable: false),
            async (operation, token) =>
            {
                await operation.ReportAsync(
                    10,
                    "Rescanning Manga chapters and pages.",
                    cancellationToken: token);

                var importer = new MangaImportService(repository);
                var imported = await importer.ImportAsync(
                    series.SourcePath,
                    token);
                var metadata = new MangaAniListService(
                    repository,
                    httpClientFactory,
                    mappingReviewStore);
                await metadata.AutoMatchAsync(
                    imported.SeriesId,
                    token);
                return imported;
            },
            "Manga source refreshed.",
            cancellationToken);

        TempData["Status"] = Ui.Format(
            "manga.status.refreshed",
            ("chapters", result.ChapterCount),
            ("pages", result.PageCount));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDirectionAsync(
        Guid id,
        string? direction,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var repository = new MangaRepository(db);
        await repository.SetDirectionAsync(
            id,
            direction ?? "rtl",
            cancellationToken);
        return RedirectToPage(new { id });
    }
}

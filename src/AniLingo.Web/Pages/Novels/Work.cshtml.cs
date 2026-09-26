using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Novels;

[NovelEpubUploadRequestLimits("UploadVolume")]
public sealed class WorkModel(
    AppDbContext db,
    NovelCatalogQueries catalog,
    NovelImportService imports,
    NovelProgressService progress,
    NovelMetadataService metadata,
    NovelMappingService mappings,
    AniListAccountService aniListAccount,
    NovelJobs jobs,
    NovelEpubImportService epubImports,
    CurrentAccountContext account,
    OperationRunner operations) : PageModel
{
    public NovelWorkDetail? Detail { get; private set; }
    public IReadOnlyList<NovelMetadataCandidate> SearchResults { get; private set; } = [];
    public IReadOnlyList<NovelAnimeChoice> AnimeChoices { get; private set; } = [];
    public NovelProgress? Progress { get; private set; }
    public ExternalProgressSummary? ExternalProgress { get; private set; }
    public string SearchQuery { get; private set; } = "";
    public bool IsSearching { get; private set; }
    public bool IsOwner => account.IsOwner;

    /// <summary>
    /// Whole-work translation state (the chapter "Translated" badges), gated
    /// the same way as the Novel reader's own translation UI: through
    /// <see cref="LearningModuleResolver.ResolveTranslationEnabledAsync"/> for
    /// this work's Novel scope, not by whether a translation is cached.
    /// </summary>
    public bool TranslationEnabled { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? q,
        CancellationToken cancellationToken)
    {
        Detail = await catalog.GetWorkDetailAsync(id, cancellationToken);
        if (Detail is null)
        {
            return NotFound();
        }

        TranslationEnabled = await new LearningModuleResolver(db).ResolveTranslationEnabledAsync(
            account.ProfileId,
            LearningMediaType.Novel,
            id.ToString(),
            contentKey: null,
            cancellationToken);

        Progress = await progress.GetProgressAsync(
            account.ProfileId,
            id,
            cancellationToken);

        // Local-only: remote AniList progress is loaded after first paint
        // through OnGetExternalProgressAsync.
        ExternalProgress = await aniListAccount.GetNovelProgressSummaryAsync(
            id,
            cancellationToken);

        AnimeChoices = account.IsOwner
            ? await mappings.GetAnimeChoicesAsync(cancellationToken)
            : [];
        SearchQuery = string.IsNullOrWhiteSpace(q)
            ? Detail.Work.MetadataTitle ?? Detail.Work.Title
            : q.Trim();

        if (account.IsOwner && !string.IsNullOrWhiteSpace(q))
        {
            IsSearching = true;
            try
            {
                SearchResults = await metadata.SearchAsync(
                    NovelAniListProvider.ProviderKey,
                    SearchQuery,
                    8,
                    cancellationToken);
            }
            catch (NovelMetadataProviderException exception)
            {
                TempData["Status"] = exception.Message;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnGetExternalProgressAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (await catalog.GetWorkTitleAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var state = await aniListAccount.GetNovelProgressStateAsync(
            id,
            cancellationToken);

        Response.Headers.CacheControl = "no-store";
        return Partial(
            "_ExternalProgressState",
            new ExternalProgressRemoteView(
                ExternalProgressMediaKind.Novel,
                state,
                "SyncAniListProgress"));
    }

    public async Task<IActionResult> OnPostSyncAniListProgressAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await operations.RunAsync(
            new OperationDescriptor(
                "anilist-novel-progress-sync",
                "AniList",
                "Sync novel progress",
                ProfileId: account.ProfileId,
                Lane: OperationLane.Normal,
                Retryable: false),
            (_, token) => aniListAccount.SyncNovelProgressAsync(
                id,
                token),
            "Novel progress sync completed.",
            cancellationToken);

        TempData["Status"] = result.Message;
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

        try
        {
            await operations.RunAsync(
                new OperationDescriptor(
                    "novel-refresh",
                    "Novels",
                    "Refresh novel table of contents",
                    ProfileId: account.ProfileId,
                    Lane: OperationLane.Normal,
                    IsDownload: true,
                    Retryable: false),
                (_, token) => imports.RefreshWorkAsync(id, token),
                "Novel table of contents refreshed.",
                cancellationToken);

            TempData["Status"] = "Table of contents refreshed.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUploadVolumeAsync(
        Guid id,
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
            targetWorkId: id,
            cancellationToken);

        TempData["Status"] = outcomes is null
            ? $"Choose one to {NovelEpubUploadRequestLimitsAttribute.MaximumFiles} EPUB files."
            : NovelEpubImportOutcome.Summarize(outcomes);

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveVolumeAsync(
        Guid id,
        Guid volumeId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await epubImports.RemoveVolumeAsync(id, volumeId, cancellationToken);
            TempData["Status"] = "Volume removed.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostLoadAllAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var title = await catalog.GetWorkTitleAsync(id, cancellationToken);
        if (title is null)
        {
            return NotFound();
        }

        var chapterIds = await catalog.GetChapterIdsWithoutContentAsync(
            id,
            cancellationToken);

        if (chapterIds.Count > 0)
        {
            await jobs.QueueChapterDownloadAsync(
                title,
                chapterIds,
                account.ProfileId,
                cancellationToken);
        }

        TempData["Status"] = chapterIds.Count == 0
            ? "All chapter text is already cached."
            : $"Queued {chapterIds.Count} chapter texts for local caching.";

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMatchMetadataAsync(
        Guid id,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await operations.RunAsync(
                new OperationDescriptor(
                    "novel-anilist-match",
                    "Novels",
                    "Match novel metadata",
                    ProfileId: account.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                (_, token) => metadata.MatchAsync(
                    id,
                    provider,
                    externalId,
                    token),
                "Novel metadata matched.",
                cancellationToken);

            TempData["Status"] = "AniList novel matched.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NovelMetadataProviderException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveMetadataAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        await metadata.RemoveAsync(id, cancellationToken);
        TempData["Status"] = "AniList novel match removed.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddMappingAsync(
        Guid id,
        Guid animeId,
        int chapterStart,
        int chapterEnd,
        int seasonNumber,
        int episodeStart,
        int episodeEnd,
        string? label,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            await mappings.AddManualAsync(
                id,
                animeId,
                chapterStart,
                chapterEnd,
                seasonNumber,
                episodeStart,
                episodeEnd,
                label,
                cancellationToken);
            TempData["Status"] = "Episode mapping saved.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSuggestMappingsAsync(
        Guid id,
        Guid animeId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var title = await catalog.GetWorkTitleAsync(id, cancellationToken);
        if (title is null)
        {
            return NotFound();
        }

        await jobs.QueueEpisodeMappingAsync(
            id,
            animeId,
            title,
            account.ProfileId,
            cancellationToken);

        TempData["Status"] = "AI episode matching queued. Refresh this page after it completes.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveMappingAsync(
        Guid id,
        Guid mappingId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        await mappings.RemoveAsync(id, mappingId, cancellationToken);
        TempData["Status"] = "Episode mapping removed.";
        return RedirectToPage(new { id });
    }
}

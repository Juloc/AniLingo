using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Tracking;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace AniLingo.Web.Pages.Novels;

public sealed class WorkModel(
    NovelService novels,
    NovelMetadataService metadata,
    NovelMappingService mappings,
    AniListAccountService aniListAccount,
    BackgroundJobQueue jobs,
    CurrentAccountContext account,
    OperationRunner operations) : PageModel
{
    public NovelWorkDetail? Detail { get; private set; }
    public IReadOnlyList<NovelMetadataCandidate> SearchResults { get; private set; } = [];
    public IReadOnlyList<NovelAnimeChoice> AnimeChoices { get; private set; } = [];
    public NovelProgress? Progress { get; private set; }
    public AniListReadingProgressPreview? AniListProgress { get; private set; }
    public string SearchQuery { get; private set; } = "";
    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? q,
        CancellationToken cancellationToken)
    {
        Detail = await novels.GetWorkAsync(id, cancellationToken);
        if (Detail is null)
        {
            return NotFound();
        }

        Progress = await novels.GetProgressAsync(
            account.ProfileId,
            id,
            cancellationToken);

        if (string.Equals(
                Detail.Work.MetadataProvider,
                NovelAniListProvider.ProviderKey,
                StringComparison.OrdinalIgnoreCase))
        {
            AniListProgress = await aniListAccount.GetNovelProgressPreviewAsync(
                id,
                cancellationToken);
        }

        AnimeChoices = account.IsOwner
            ? await mappings.GetAnimeChoicesAsync(cancellationToken)
            : [];
        SearchQuery = string.IsNullOrWhiteSpace(q)
            ? Detail.Work.MetadataTitle ?? Detail.Work.Title
            : q.Trim();

        if (account.IsOwner && !string.IsNullOrWhiteSpace(q))
        {
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
                (_, token) => novels.RefreshWorkAsync(id, token),
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

    public async Task<IActionResult> OnPostLoadAllAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var detail = await novels.GetWorkAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        var chapterIds = detail.Chapters
            .Where(x => !x.HasContent)
            .Select(x => x.Id)
            .ToArray();

        await jobs.QueueAsync(
            new OperationDescriptor(
                "novel-chapter-download",
                "Novels",
                "Download novel chapters",
                detail.Work.MetadataTitle ?? detail.Work.Title,
                account.ProfileId,
                OperationLane.Normal,
                IsDownload: true,
                Retryable: true),
            async (operation, services, workerToken) =>
            {
                var service = services.GetRequiredService<NovelService>();

                if (chapterIds.Length == 0)
                {
                    await operation.ReportAsync(
                        100,
                        "All chapter text is already cached.",
                        cancellationToken: workerToken);
                    return;
                }

                for (var index = 0; index < chapterIds.Length; index++)
                {
                    await operation.ReportAsync(
                        Math.Clamp((int)Math.Round(index * 100d / chapterIds.Length), 0, 99),
                        $"Downloading chapter {index + 1} of {chapterIds.Length}.",
                        cancellationToken: workerToken);

                    await service.EnsureChapterContentAsync(
                        chapterIds[index],
                        forceRefresh: false,
                        workerToken);
                    await Task.Delay(150, workerToken);
                }

                await operation.ReportAsync(
                    100,
                    $"Downloaded {chapterIds.Length} chapter(s).",
                    cancellationToken: workerToken);
            },
            cancellationToken);

        TempData["Status"] = chapterIds.Length == 0
            ? "All chapter text is already cached."
            : $"Queued {chapterIds.Length} chapter texts for local caching.";

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

        await jobs.QueueAsync(
            new OperationDescriptor(
                "novel-episode-mapping",
                "AI",
                "Suggest novel episode mappings",
                Detail?.Work.MetadataTitle ?? Detail?.Work.Title ?? "Novel",
                account.ProfileId,
                OperationLane.Normal,
                Retryable: true),
            async (operation, services, workerToken) =>
            {
                await operation.ReportAsync(
                    5,
                    "Generating mapping suggestions.",
                    cancellationToken: workerToken);

                var service = services.GetRequiredService<NovelMappingService>();
                await service.SuggestAsync(id, animeId, workerToken);

                await operation.ReportAsync(
                    100,
                    "Mapping suggestions are ready.",
                    cancellationToken: workerToken);
            },
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

using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Novels;
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
    CurrentAccountContext account) : PageModel
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
        var result = await aniListAccount.SyncNovelProgressAsync(
            id,
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
            await novels.RefreshWorkAsync(id, cancellationToken);
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
            async (services, workerToken) =>
            {
                var service = services.GetRequiredService<NovelService>();
                foreach (var chapterId in chapterIds)
                {
                    await service.EnsureChapterContentAsync(
                        chapterId,
                        forceRefresh: false,
                        workerToken);
                    await Task.Delay(150, workerToken);
                }
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
            await metadata.MatchAsync(id, provider, externalId, cancellationToken);
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
            async (services, workerToken) =>
            {
                var service = services.GetRequiredService<NovelMappingService>();
                await service.SuggestAsync(id, animeId, workerToken);
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

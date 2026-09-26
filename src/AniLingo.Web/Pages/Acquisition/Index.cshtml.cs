using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.DownloadClients;
using AniLingo.Web.Features.Acquisition.History;
using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Monitoring;
using AniLingo.Web.Features.Acquisition.Pipeline;
using AniLingo.Web.Features.Acquisition.Prowlarr;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Acquisition;

/// <summary>
/// Owner overview of anime acquisition: schedule, monitored anime, wanted episodes, active
/// downloads, imports that need a decision and recent decisions with their reasons. Interactive
/// search results are shown on the same page so a grab stays one step away.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class IndexModel(
    AnimeAcquisitionPipeline pipeline,
    AnimeAcquisitionScheduler scheduler,
    AnimeImportExecutor importExecutor,
    DownloadClientStore downloadClients,
    AcquisitionHistoryService history,
    AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Season { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Episode { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProwlarrAnimeSearchMode Mode { get; set; } = ProwlarrAnimeSearchMode.Episode;

    public AnimeAcquisitionOverview Overview { get; private set; } = null!;
    public AnimeInteractiveSearch? SearchResult { get; private set; }
    public bool ProwlarrConfigured { get; private set; }
    public bool SabnzbdConfigured { get; private set; }
    public string? ConfigurationError { get; private set; }
    public IReadOnlyList<AcquisitionHistoryEntry> RecentHistory { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> AnimeTitles { get; private set; } = new Dictionary<Guid, string>();
    public AnimeAcquisitionScheduler Scheduler => scheduler;
    public string? Notice => TempData["AcquisitionNotice"] as string;
    public string? Error => TempData["AcquisitionError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ProwlarrConfigured = await pipeline.IsProwlarrConfiguredAsync(cancellationToken);
        SabnzbdConfigured = (await downloadClients.LoadAllAsync(cancellationToken))
            .Any(entry => entry.Enabled && entry.Type == DownloadClientType.Sabnzbd);
        Overview = await pipeline.GetOverviewAsync(cancellationToken);
        RecentHistory = await history.RecentAsync(30, cancellationToken);
        if (RecentHistory.Count > 0)
        {
            var animeIds = RecentHistory.Select(entry => entry.AnimeId).Distinct().ToArray();
            AnimeTitles = await db.Anime
                .AsNoTracking()
                .Where(item => animeIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.Title, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            SearchResult = await pipeline.SearchInteractiveAsync(
                Search.Trim(),
                Mode == ProwlarrAnimeSearchMode.Anime ? null : Season,
                Mode == ProwlarrAnimeSearchMode.Episode ? Episode : null,
                Mode,
                cancellationToken);
            if (SearchResult is null)
            {
                ConfigurationError = "The anime to search no longer exists.";
            }
        }
    }

    public async Task<IActionResult> OnPostScheduleAsync(
        bool enabled,
        int intervalMinutes,
        CancellationToken cancellationToken)
    {
        if (intervalMinutes is < AnimeMonitoringSchedule.MinimumIntervalMinutes or > AnimeMonitoringSchedule.MaximumIntervalMinutes)
        {
            TempData["AcquisitionError"] =
                $"The interval must be between {AnimeMonitoringSchedule.MinimumIntervalMinutes} and {AnimeMonitoringSchedule.MaximumIntervalMinutes} minutes.";
            return RedirectToPage();
        }

        await pipeline.UpdateScheduleAsync(enabled, intervalMinutes, cancellationToken);
        TempData["AcquisitionNotice"] = enabled
            ? $"Automatic search runs every {intervalMinutes} minutes."
            : "Automatic search is off; searches run only when requested.";
        return RedirectToPage();
    }

    public IActionResult OnPostRunNow()
    {
        SetQueued(scheduler.RequestRun(), "Search for all monitored anime queued. Decisions appear below as they are made.");
        return RedirectToPage();
    }

    public IActionResult OnPostSearchAnime(string animeKey, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(animeKey))
        {
            return BadRequest();
        }

        SetQueued(scheduler.RequestRun(animeKey.Trim()), "Search queued. Decisions appear on the acquisition overview.");
        return RedirectBack(returnUrl);
    }

    private void SetQueued(bool queued, string notice)
    {
        if (queued)
        {
            TempData["AcquisitionNotice"] = notice;
        }
        else
        {
            TempData["AcquisitionError"] = "Too many searches are already queued; try again when they finished.";
        }
    }

    public async Task<IActionResult> OnPostAnimeSettingsAsync(
        Guid animeId,
        bool monitored,
        bool searchOnAdd,
        string? profileId,
        string? indexerIds,
        string[]? tagIds,
        Guid? targetRootId,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (!TryParseIds(indexerIds, out var ids))
        {
            TempData["AcquisitionError"] = "Indexer IDs must be positive numbers separated by commas.";
            return RedirectBack(returnUrl);
        }

        try
        {
            var update = await pipeline.UpdateAnimeSettingsAsync(
                animeId,
                monitored,
                searchOnAdd,
                string.IsNullOrWhiteSpace(profileId) ? null : profileId.Trim(),
                ids,
                cancellationToken,
                tagIds,
                targetRootId);
            if (update is null)
            {
                return NotFound();
            }

            var queued = update.StartedMonitoring && searchOnAdd &&
                         scheduler.RequestRun(update.AnimeKey, AnimeSearchTrigger.SearchOnAdd);
            TempData["AcquisitionNotice"] = queued
                ? "Acquisition settings saved; wanted episodes are being searched."
                : "Acquisition settings saved.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            TempData["AcquisitionError"] = exception.Message;
        }

        return RedirectBack(returnUrl);
    }

    public async Task<IActionResult> OnPostGrabAsync(
        string animeKey,
        int? season,
        int? episode,
        ProwlarrAnimeSearchMode mode,
        string releaseIdentity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(animeKey) || string.IsNullOrWhiteSpace(releaseIdentity))
        {
            return BadRequest();
        }

        var result = await scheduler.RunExclusiveAsync(
            (runner, token) => runner.GrabAsync(animeKey, season, episode, mode, releaseIdentity, token),
            cancellationToken);
        TempData[result.Success ? "AcquisitionNotice" : "AcquisitionError"] = result.Message;
        return result.Success
            ? RedirectToPage()
            : RedirectToPage(new { search = animeKey, season, episode, mode });
    }

    public async Task<IActionResult> OnPostManualImportAsync(
        Guid recordId,
        string sourcePath,
        int season,
        int episode,
        CancellationToken cancellationToken)
    {
        var result = await importExecutor.ImportManuallyAsync(recordId, sourcePath, season, episode, cancellationToken);
        if (result.Success)
        {
            await scheduler.RunExclusiveAsync(
                async (runner, token) =>
                {
                    await runner.ReconcileAttemptsAsync(token);
                    return true;
                },
                cancellationToken);
        }

        TempData[result.Success ? "AcquisitionNotice" : "AcquisitionError"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDismissImportAsync(
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var result = await importExecutor.DismissAsync(recordId, cancellationToken);
        TempData[result.Success ? "AcquisitionNotice" : "AcquisitionError"] = result.Message;
        return RedirectToPage();
    }

    private IActionResult RedirectBack(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToPage();

    private static bool TryParseIds(string? value, out int[] ids)
    {
        ids = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var parsed = new List<int>();
        foreach (var part in value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var id) || id <= 0)
            {
                return false;
            }

            parsed.Add(id);
        }

        ids = [.. parsed.Distinct().Order()];
        return true;
    }
}

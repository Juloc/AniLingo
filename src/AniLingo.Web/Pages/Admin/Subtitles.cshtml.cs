using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Subtitles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Admin;

public sealed class SubtitlesModel(
    SubtitleImportService subtitleImportService) : PageModel
{
    public LearningTextCoverageSnapshot Coverage { get; private set; } =
        new(0, 0, 0, 0, 0, 0);

    public JimakuConnectionStatus Jimaku { get; private set; } =
        new(false);

    public IReadOnlyList<LearningTextEpisodeStatus> MissingEpisodes { get; private set; } = [];

    [BindProperty]
    public string JimakuApiKey { get; set; } = "";

    public string? StatusMessage => TempData["Status"] as string;
    public string? ErrorMessage => TempData["Error"] as string;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AccountRoles.Owner))
        {
            return Forbid();
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveJimakuAsync(
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AccountRoles.Owner))
        {
            return Forbid();
        }

        var result = await subtitleImportService.SaveJimakuApiKeyAsync(
            JimakuApiKey,
            cancellationToken);

        TempData[result.Success ? "Status" : "Error"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectJimakuAsync(
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AccountRoles.Owner))
        {
            return Forbid();
        }

        await subtitleImportService.DisconnectJimakuAsync(cancellationToken);
        TempData["Status"] = "Jimaku connection removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPrepareAllAsync(
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AccountRoles.Owner))
        {
            return Forbid();
        }

        var count = await subtitleImportService.QueueAllMissingAsync(
            cancellationToken);

        TempData["Status"] = count == 0
            ? "No new learning-text preparation jobs were queued."
            : $"Queued learning-text preparation for {count} episode(s).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AccountRoles.Owner))
        {
            return Forbid();
        }

        var queued = await subtitleImportService.QueueLearningTextAsync(
            episodeId,
            cancellationToken);

        TempData["Status"] = queued
            ? "Learning-text preparation queued."
            : "Episode is already ready or currently being prepared.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Coverage = await subtitleImportService.GetCoverageAsync(cancellationToken);
        Jimaku = await subtitleImportService.GetJimakuConnectionStatusAsync(
            cancellationToken);
        MissingEpisodes = await subtitleImportService.GetMissingEpisodesAsync(
            200,
            cancellationToken);
    }
}

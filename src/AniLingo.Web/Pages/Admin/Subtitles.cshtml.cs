using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Subtitles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Admin;

[Authorize(Roles = AccountRoles.Owner)]
public sealed class SubtitlesModel(
    AppDbContext db,
    SubtitleImportService subtitleImportService) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

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

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        await subtitleImportService.DisconnectJimakuAsync(cancellationToken);
        TempData["Status"] = Ui["admin.subtitles.jimakuRemoved"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPrepareAllAsync(
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AccountRoles.Owner))
        {
            return Forbid();
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var count = await subtitleImportService.QueueAllMissingAsync(
            cancellationToken);

        TempData["Status"] = count == 0
            ? Ui["admin.subtitles.noneQueued"]
            : Ui.Format("admin.subtitles.queuedCount", ("count", count));
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

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var queued = await subtitleImportService.QueueLearningTextAsync(
            episodeId,
            cancellationToken);

        TempData["Status"] = queued
            ? Ui["admin.subtitles.preparationQueued"]
            : Ui["admin.subtitles.episodeAlreadyReady"];
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

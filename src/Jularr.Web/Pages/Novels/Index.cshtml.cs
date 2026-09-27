using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Operations;
using Jularr.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Novels;

[NovelEpubUploadRequestLimits("UploadEpub")]
public sealed class IndexModel(
    NovelCatalogQueries catalog,
    NovelImportService imports,
    NovelEpubImportService epubImports,
    BookCatalogService books,
    CurrentAccountContext account,
    OperationRunner operations,
    AppDbContext db) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<NovelListItem> Works { get; private set; } = [];
    public IReadOnlyList<NovelListItem> ContinueReading { get; private set; } = [];
    public bool IsOwner => account.IsOwner;
    public bool IsInboxConfigured => books.IsInboxConfigured;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Works = await catalog.GetLibraryAsync(account.ProfileId, cancellationToken);
        ContinueReading = Works
            .Where(x => x.HasProgress)
            .OrderByDescending(x => x.LastReadAt)
            .Take(8)
            .ToArray();
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

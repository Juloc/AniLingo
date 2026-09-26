using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Novels;

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
    public IReadOnlyList<NovelListItem> Works { get; private set; } = [];
    public IReadOnlyList<NovelListItem> ContinueReading { get; private set; } = [];
    public bool IsOwner => account.IsOwner;
    public bool IsInboxConfigured => books.IsInboxConfigured;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
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

            TempData["Status"] = "Novel imported. Chapter text is loaded on demand.";
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
            TempData["Status"] =
                $"Choose one to {NovelEpubUploadRequestLimitsAttribute.MaximumFiles} EPUB files.";
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
            TempData["Status"] = "Configure the reading inbox in Books → Integrations first.";
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

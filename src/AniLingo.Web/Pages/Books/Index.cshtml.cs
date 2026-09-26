using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class IndexModel(
    BookCatalogService books,
    CurrentAccountContext account,
    AppDbContext db,
    SabnzbdConnectionResolver sabnzbdSettings,
    SabnzbdDownloadService sabnzbd,
    OperationRunner operations) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public string Query { get; private set; } = "";
    public string TargetLanguage { get; private set; } = "id";
    public IReadOnlyList<BookCatalogItem> Results { get; private set; } = [];
    public IReadOnlyList<BookLibraryItem> Library { get; private set; } = [];
    public string? Error { get; private set; }
    public bool IsOwner => account.IsOwner;
    public bool IsSabnzbdConfigured { get; private set; }
    public bool IsInboxConfigured => books.IsInboxConfigured;

    public async Task OnGetAsync(
        string? q,
        string? lang,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Query = q?.Trim() ?? "";
        TargetLanguage = BookLanguageCatalog.Normalize(lang);
        IsSabnzbdConfigured = account.IsOwner
            && (await sabnzbdSettings.ResolveAsync(cancellationToken)).IsConfigured;

        Library = await books.GetLibraryAsync(
            account.ProfileId,
            TargetLanguage,
            cancellationToken);

        if (Query.Length == 0)
        {
            return;
        }

        try
        {
            Results = await books.SearchAsync(
                Query,
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Error = Ui["books.index.searchTimeout"];
        }
        catch (HttpRequestException)
        {
            Error = Ui["books.index.searchUnavailable"];
        }
        catch (InvalidOperationException exception)
        {
            Error = exception.Message;
        }
    }

    public async Task<IActionResult> OnPostUploadAsync(
        IFormFile? epub,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (epub is null || epub.Length == 0)
        {
            TempData["Status"] = ui["books.index.chooseEpubFirst"];
            return RedirectToPage();
        }

        if (!epub.FileName.EndsWith(
                ".epub",
                StringComparison.OrdinalIgnoreCase))
        {
            TempData["Status"] = ui["books.index.epubOnly"];
            return RedirectToPage();
        }

        try
        {
            var workId = await operations.RunAsync(
                new OperationDescriptor(
                    "book-epub-upload-import",
                    "Books",
                    "Import uploaded EPUB",
                    epub.FileName,
                    account.ProfileId,
                    OperationLane.Normal,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        "Parsing uploaded EPUB.",
                        cancellationToken: token);

                    await using var stream = epub.OpenReadStream();
                    return await books.ImportUploadedEpubAsync(
                        stream,
                        epub.FileName,
                        token);
                },
                "Uploaded EPUB imported.",
                cancellationToken);

            return RedirectToPage(
                "/Books/Library",
                new { id = workId, lang = "id" });
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostRemoteEpubAsync(
        string? epubUrl,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var operationStore = new OperationStore(db);
        var operationId = await operationStore.CreateAsync(
            new OperationDescriptor(
                "remote-epub-import",
                "Books",
                "Download remote EPUB",
                ProfileId: account.ProfileId,
                Lane: OperationLane.Normal,
                IsDownload: true,
                Retryable: false),
            cancellationToken);

        await operationStore.MarkRunningAsync(operationId, cancellationToken);

        try
        {
            var workId = await books.ImportRemoteEpubAsync(
                epubUrl ?? "",
                cancellationToken);

            await operationStore.MarkSucceededAsync(
                operationId,
                "EPUB downloaded and imported.",
                cancellationToken);

            return RedirectToPage(
                "/Books/Library",
                new { id = workId, lang = "id" });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or HttpRequestException
                or TaskCanceledException)
        {
            await operationStore.MarkFailedAsync(
                operationId,
                $"{exception.GetType().Name}: {exception.Message}",
                CancellationToken.None);
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostImportInboxAsync(
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            var imported = await BookInboxImport.RunAsync(
                operations,
                books,
                account.ProfileId,
                cancellationToken);

            TempData["Status"] = imported.Count == 0
                ? ui["books.index.inboxEmpty"]
                : ui.Format("books.index.inboxImported", ("count", imported.Count));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSabUrlAsync(
        string? nzbUrl,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        var effectiveName = string.IsNullOrWhiteSpace(displayName)
            ? ui["books.index.defaultBookName"]
            : displayName.Trim();

        try
        {
            var outcome = await sabnzbd.SubmitUrlAsync(
                BookSabnzbdSubmission(effectiveName),
                SabnzbdDownloadService.ParseNzbUrl(nzbUrl),
                cancellationToken);
            TempData["Status"] = outcome.Message;
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSabFileAsync(
        IFormFile? nzb,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (nzb is null || nzb.Length == 0)
        {
            TempData["Status"] = ui["books.index.chooseNzbFirst"];
            return RedirectToPage();
        }

        try
        {
            await using var stream = nzb.OpenReadStream();
            var outcome = await sabnzbd.SubmitFileAsync(
                BookSabnzbdSubmission(nzb.FileName),
                stream,
                nzb.FileName,
                cancellationToken);
            TempData["Status"] = outcome.Message;
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }

    private SabnzbdSubmission BookSabnzbdSubmission(string name) =>
        new(
            BookInboxImport.SabnzbdDownloadKind,
            "SABnzbd download",
            name,
            account.ProfileId,
            SabnzbdPurpose.Books,
            JobName: name);
}

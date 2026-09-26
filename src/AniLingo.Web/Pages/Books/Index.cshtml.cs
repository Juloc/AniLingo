using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
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
            Error = "Book search timed out. Please try again.";
        }
        catch (HttpRequestException)
        {
            Error = "Book search is temporarily unavailable. Please try again.";
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
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (epub is null || epub.Length == 0)
        {
            TempData["Status"] = "Choose an EPUB file first.";
            return RedirectToPage();
        }

        if (!epub.FileName.EndsWith(
                ".epub",
                StringComparison.OrdinalIgnoreCase))
        {
            TempData["Status"] = "Only EPUB files are supported for book import.";
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
                ? "No EPUB files were found in the Books inbox."
                : $"Books inbox imported {imported.Count} book(s).";
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
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var effectiveName = string.IsNullOrWhiteSpace(displayName)
            ? "AniLingo book"
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
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (nzb is null || nzb.Length == 0)
        {
            TempData["Status"] = "Choose an NZB file first.";
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

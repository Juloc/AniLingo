using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class IndexModel(
    BookCatalogService books,
    CurrentAccountContext account) : PageModel
{
    public string Query { get; private set; } = "";
    public string TargetLanguage { get; private set; } = "id";
    public IReadOnlyList<BookCatalogItem> Results { get; private set; } = [];
    public IReadOnlyList<BookLibraryItem> Library { get; private set; } = [];
    public string? Error { get; private set; }
    public bool IsOwner => account.IsOwner;
    public bool IsSabnzbdConfigured => books.IsSabnzbdConfigured;
    public bool IsInboxConfigured => books.IsInboxConfigured;

    public async Task OnGetAsync(
        string? q,
        string? lang,
        CancellationToken cancellationToken)
    {
        Query = q?.Trim() ?? "";
        TargetLanguage = BookLanguageCatalog.Normalize(lang);

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
            await using var stream = epub.OpenReadStream();
            var workId = await books.ImportUploadedEpubAsync(
                stream,
                epub.FileName,
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

    public async Task<IActionResult> OnPostImportInboxAsync(
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            var imported = await books.ImportInboxAsync(
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

        try
        {
            var result = await books.QueueSabnzbdUrlAsync(
                nzbUrl ?? "",
                displayName,
                cancellationToken);
            TempData["Status"] = result.Message;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or HttpRequestException
                or TaskCanceledException)
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
            var result = await books.QueueSabnzbdFileAsync(
                stream,
                nzb.FileName,
                cancellationToken);
            TempData["Status"] = result.Message;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or HttpRequestException
                or TaskCanceledException)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage();
    }
}

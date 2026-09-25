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
    SabnzbdOperationsClient sabnzbdOperations,
    OperationRunner operations) : PageModel
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

        if (account.IsOwner && books.IsInboxConfigured)
        {
            try
            {
                var imported = await books.ImportInboxAsync(
                    cancellationToken);
                if (imported.Count > 0)
                {
                    TempData["Status"] =
                        $"Automatically imported {imported.Count} book(s) from the Books inbox.";
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or IOException
                    or UnauthorizedAccessException)
            {
                // The inbox is optional. A missing/offline download mount
                // must not make the local Books library unavailable.
            }
        }

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
            var imported = await operations.RunAsync(
                new OperationDescriptor(
                    "book-inbox-import",
                    "Books",
                    "Import Books inbox",
                    ProfileId: account.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        "Scanning Books inbox.",
                        cancellationToken: token);

                    return await books.ImportInboxAsync(token);
                },
                "Books inbox scan completed.",
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

        var operationStore = new OperationStore(db);
        var operationId = await operationStore.CreateAsync(
            new OperationDescriptor(
                "sabnzbd-download",
                "External downloads",
                "SABnzbd download",
                effectiveName,
                account.ProfileId,
                OperationLane.Normal,
                IsDownload: true,
                Retryable: false,
                ExternalProvider: SabnzbdOperationsClient.ProviderId),
            cancellationToken);

        await operationStore.MarkRunningAsync(operationId, cancellationToken);
        await operationStore.ReportProgressAsync(
            operationId,
            0,
            "Submitting download to SABnzbd.",
            cancellationToken: cancellationToken);

        IReadOnlySet<string>? existingIds = null;
        try
        {
            existingIds = await sabnzbdOperations.CaptureJobIdsAsync(
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidOperationException)
        {
            await operationStore.AppendLogAsync(
                operationId,
                OperationLogLevel.Warning,
                "SABnzbd",
                "Live queue access is unavailable; AniLingo will still try to submit the download.",
                CancellationToken.None);
        }

        try
        {
            var result = await books.QueueSabnzbdUrlAsync(
                nzbUrl ?? "",
                effectiveName,
                cancellationToken);

            if (!result.Accepted)
            {
                await operationStore.MarkFailedAsync(
                    operationId,
                    result.Message,
                    CancellationToken.None);
                TempData["Status"] = result.Message;
                return RedirectToPage();
            }

            var externalId = existingIds is null
                ? null
                : await sabnzbdOperations.ResolveNewJobIdAsync(
                    existingIds,
                    effectiveName,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(externalId))
            {
                await operationStore.MarkSucceededAsync(
                    operationId,
                    "Sent to SABnzbd. Live progress could not be linked; use a full SABnzbd API key to enable queue/history monitoring.",
                    CancellationToken.None);
            }
            else
            {
                await operationStore.SetExternalReferenceAsync(
                    operationId,
                    SabnzbdOperationsClient.ProviderId,
                    externalId,
                    cancellationToken);
                await operationStore.ReportProgressAsync(
                    operationId,
                    0,
                    "Accepted by SABnzbd; waiting for download progress.",
                    cancellationToken: cancellationToken);
            }

            TempData["Status"] =
                "SABnzbd download submitted. Track it under Admin → Operations → Downloads.";
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

        var operationStore = new OperationStore(db);
        var operationId = await operationStore.CreateAsync(
            new OperationDescriptor(
                "sabnzbd-download",
                "External downloads",
                "SABnzbd download",
                nzb.FileName,
                account.ProfileId,
                OperationLane.Normal,
                IsDownload: true,
                Retryable: false,
                ExternalProvider: SabnzbdOperationsClient.ProviderId),
            cancellationToken);

        await operationStore.MarkRunningAsync(operationId, cancellationToken);
        await operationStore.ReportProgressAsync(
            operationId,
            0,
            "Submitting NZB to SABnzbd.",
            cancellationToken: cancellationToken);

        IReadOnlySet<string>? existingIds = null;
        try
        {
            existingIds = await sabnzbdOperations.CaptureJobIdsAsync(
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidOperationException)
        {
            await operationStore.AppendLogAsync(
                operationId,
                OperationLogLevel.Warning,
                "SABnzbd",
                "Live queue access is unavailable; AniLingo will still try to submit the NZB.",
                CancellationToken.None);
        }

        try
        {
            await using var stream = nzb.OpenReadStream();
            var result = await books.QueueSabnzbdFileAsync(
                stream,
                nzb.FileName,
                cancellationToken);

            if (!result.Accepted)
            {
                await operationStore.MarkFailedAsync(
                    operationId,
                    result.Message,
                    CancellationToken.None);
                TempData["Status"] = result.Message;
                return RedirectToPage();
            }

            var externalId = existingIds is null
                ? null
                : await sabnzbdOperations.ResolveNewJobIdAsync(
                    existingIds,
                    nzb.FileName,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(externalId))
            {
                await operationStore.MarkSucceededAsync(
                    operationId,
                    "Sent to SABnzbd. Live progress could not be linked; use a full SABnzbd API key to enable queue/history monitoring.",
                    CancellationToken.None);
            }
            else
            {
                await operationStore.SetExternalReferenceAsync(
                    operationId,
                    SabnzbdOperationsClient.ProviderId,
                    externalId,
                    cancellationToken);
                await operationStore.ReportProgressAsync(
                    operationId,
                    0,
                    "Accepted by SABnzbd; waiting for download progress.",
                    cancellationToken: cancellationToken);
            }

            TempData["Status"] =
                "SABnzbd download submitted. Track it under Admin → Operations → Downloads.";
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
        }

        return RedirectToPage();
    }

}

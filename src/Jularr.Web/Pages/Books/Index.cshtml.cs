using System.Text.Json;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Jularr.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Books;

public sealed class IndexModel(
    BookCatalogService books,
    CurrentAccountContext account,
    AppDbContext db,
    DownloadClientStore downloadClients,
    SabnzbdDownloadService sabnzbd,
    OperationRunner operations,
    AcquisitionRequestService requests,
    AcquisitionAccessStore requestStore) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public string Query { get; private set; } = "";
    public string TargetLanguage { get; private set; } = "id";
    public IReadOnlyList<BookLibraryItem> Library { get; private set; } = [];
    public bool IsOwner => account.IsOwner;
    public AcquisitionCapabilities Access { get; private set; } =
        AcquisitionCapabilities.Resolve(AcquisitionAccessPolicy.Default(MediaAcquisitionKind.Book), false);
    /// <summary>The current profile's own book requests, newest first.</summary>
    public IReadOnlyList<AcquisitionRequest> MyRequests { get; private set; } = [];
    public IReadOnlyList<string> Genres => Library.SelectMany(book => book.Subjects).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(40).ToArray();
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
        Access = await requests.GetCapabilitiesAsync(MediaAcquisitionKind.Book, cancellationToken);
        MyRequests = await requestStore.ListAsync(MediaAcquisitionKind.Book, account.ProfileId, openOnly: false, limit: 50, cancellationToken);
        IsSabnzbdConfigured = Access.CanAddManually
            && (await downloadClients.LoadAllAsync(cancellationToken))
                .Any(entry => entry.Enabled && entry.Type == DownloadClientType.Sabnzbd);

        Library = await books.GetLibraryAsync(
            account.ProfileId,
            TargetLanguage,
            cancellationToken);

        // Catalog search runs in the Add book dialog (OnGetSearchAsync); ?q= only pre-fills it.
    }

    public async Task<IActionResult> OnPostUploadAsync(
        IFormFile? epub,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!await CanAddManuallyAsync(cancellationToken))
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
        if (!await CanAddManuallyAsync(cancellationToken))
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

        if (!await CanAddManuallyAsync(cancellationToken))
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

        if (!await CanAddManuallyAsync(cancellationToken))
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

        if (!await CanAddManuallyAsync(cancellationToken))
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

    /// <summary>Catalog search for the add dialog (JSON), annotated with library and request state.</summary>
    public async Task<IActionResult> OnGetSearchAsync(string? q, CancellationToken cancellationToken)
    {
        var query = q?.Trim() ?? "";
        if (query.Length < 2)
        {
            return new JsonResult(new { results = Array.Empty<object>() });
        }

        IReadOnlyList<BookCatalogItem> found;
        try
        {
            found = await books.SearchAsync(query, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
            return new JsonResult(new { results = Array.Empty<object>(), error = ui["books.index.searchUnavailable"] });
        }

        var libraryTitles = (await books.GetLibraryAsync(account.ProfileId, BookLanguageCatalog.Normalize(null), cancellationToken))
            .GroupBy(book => NormalizeTitle(book.Title))
            .ToDictionary(group => group.Key, group => (Guid?)group.First().WorkId);
        var open = (await requestStore.ListAsync(MediaAcquisitionKind.Book, null, openOnly: true, limit: 500, cancellationToken))
            .ToDictionary(request => request.ExternalId, request => request);

        return new JsonResult(new
        {
            results = found.Take(24).Select(item => new
            {
                item.Id,
                item.Title,
                item.Author,
                item.CoverImageUrl,
                item.FirstPublishYear,
                item.SourceName,
                freeEdition = item.CanAcquire,
                libraryWorkId = libraryTitles.GetValueOrDefault(NormalizeTitle(item.Title)),
                requestStatus = open.TryGetValue(item.Id, out var request) ? AcquisitionAccessNames.Status(request.Status) : null
            })
        });
    }

    /// <summary>Adds (automatic) or requests a catalog book according to the Books access policy.</summary>
    public async Task<IActionResult> OnPostAddAsync(
        string? catalogId,
        string? title,
        string? author,
        string? coverImageUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(catalogId) || string.IsNullOrWhiteSpace(title))
        {
            return BadRequest();
        }

        try
        {
            var request = await requests.SubmitAsync(
                new AcquisitionRequestDraft(
                    MediaAcquisitionKind.Book,
                    "books-catalog",
                    catalogId.Trim(),
                    title.Trim(),
                    string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                    string.IsNullOrWhiteSpace(coverImageUrl) ? null : coverImageUrl.Trim(),
                    JsonSerializer.Serialize(new BookRequestPayload(catalogId.Trim(), title.Trim(), author?.Trim()), JsonSerializerOptions.Web)),
                cancellationToken);
            return new JsonResult(new
            {
                status = AcquisitionAccessNames.Status(request.Status),
                message = request.StatusMessage,
                resultUrl = request.ResultUrl
            });
        }
        catch (AcquisitionAccessDeniedException)
        {
            return Forbid();
        }
    }

    public async Task<IActionResult> OnPostCancelRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await requests.CancelAsync(id, cancellationToken);
        }
        catch (AcquisitionAccessDeniedException)
        {
            return Forbid();
        }

        return RedirectToPage();
    }

    private async Task<bool> CanAddManuallyAsync(CancellationToken cancellationToken) =>
        (await requests.GetCapabilitiesAsync(MediaAcquisitionKind.Book, cancellationToken)).CanAddManually;

    private static string NormalizeTitle(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private SabnzbdSubmission BookSabnzbdSubmission(string name) =>
        new(
            BookInboxImport.SabnzbdDownloadKind,
            "SABnzbd download",
            name,
            account.ProfileId,
            SabnzbdPurpose.Books,
            JobName: name);
}

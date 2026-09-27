using System.Text.Json;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Operations;
using Jularr.Web.Data;
using Microsoft.AspNetCore.DataProtection;
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
    AcquisitionAccessStore requestStore,
    MediaInboxImportService inboxes,
    IDataProtectionProvider dataProtectionProvider) : PageModel
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
    public bool IsInboxConfigured { get; private set; }

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
        IsInboxConfigured = await inboxes.InboxAsync(MediaAcquisitionKind.Book, cancellationToken) is not null;

        Library = await books.GetLibraryAsync(
            account.ProfileId,
            TargetLanguage,
            cancellationToken);

        // Catalog search runs in the Add book dialog (OnGetSearchAsync); ?q= only pre-fills it.
    }

    public async Task<IActionResult> OnPostUploadAsync(
        IFormFile? book,
        CancellationToken cancellationToken)
    {
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!await CanAddManuallyAsync(cancellationToken))
        {
            return Forbid();
        }

        if (book is null || book.Length == 0)
        {
            TempData["Status"] = ui["books.index.chooseEpubFirst"];
            return RedirectToPage();
        }

        var format = BookFileFormats.FromPath(book.FileName);
        if (format is null)
        {
            TempData["Status"] = ui["books.index.epubOnly"];
            return RedirectToPage();
        }

        var isPdf = format == BookFileFormats.Pdf;
        try
        {
            var workId = await operations.RunAsync(
                new OperationDescriptor(
                    isPdf ? "book-pdf-upload-import" : "book-epub-upload-import",
                    "Books",
                    isPdf ? "Import uploaded PDF" : "Import uploaded EPUB",
                    book.FileName,
                    account.ProfileId,
                    OperationLane.Normal,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        isPdf ? "Storing uploaded PDF." : "Parsing uploaded EPUB.",
                        cancellationToken: token);

                    await using var stream = book.OpenReadStream();
                    return isPdf
                        ? await books.ImportUploadedPdfAsync(stream, book.FileName, token)
                        : await books.ImportUploadedEpubAsync(stream, book.FileName, token);
                },
                isPdf ? "Uploaded PDF imported." : "Uploaded EPUB imported.",
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
            var imported = await inboxes.RunAsync(
                MediaAcquisitionKind.Book,
                account.ProfileId,
                cancellationToken);

            TempData["Status"] = imported.Imported == 0
                ? ui["books.index.inboxEmpty"]
                : ui.Format("books.index.inboxImported", ("count", imported.Imported));
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

        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        IReadOnlyList<BookCatalogItem> found;
        try
        {
            found = await books.SearchAsync(query, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new JsonResult(new { results = Array.Empty<object>(), error = ui["books.index.searchUnavailable"] });
        }

        var hardcover = await new BookHardcoverAccountStore(
                dataProtectionProvider)
            .LoadAsync(account.ProfileId, cancellationToken);
        var items = (await books.EnrichHardcoverStatesAsync(
                found.Take(24).ToArray(),
                hardcover,
                cancellationToken))
            .ToArray();
        var states = await new BookAddStateQuery(db, requestStore).GetAsync(
            items.Select(item => new BookAddLookup(item.Id, item.Title, item.Identities)).ToArray(),
            cancellationToken);

        return new JsonResult(new
        {
            results = items.Select(item =>
            {
                var state = states.GetValueOrDefault(item.Id) ?? BookAddState.None;
                return new
                {
                    // A request made through another provider's record of this work owns the row.
                    id = state.RequestCatalogId ?? item.Id,
                    item.Title,
                    item.Author,
                    item.CoverImageUrl,
                    covers = item.CoverCandidates,
                    year = item.FirstPublishYear,
                    summary = ShortSummary(item.Summary),
                    listState = ListStateKey(item.ExternalListState) is { } listStateKey
                        ? ui[listStateKey]
                        : null,
                    freeEdition = item.CanAcquire,
                    state = StateJson(state)
                };
            })
        });
    }

    private static string? ListStateKey(string? state) =>
        state switch
        {
            BookListStates.WantToRead => "books.listState.wantToRead",
            BookListStates.Reading => "books.listState.reading",
            BookListStates.Read => "books.listState.read",
            BookListStates.Paused => "books.listState.paused",
            BookListStates.DidNotFinish => "books.listState.didNotFinish",
            _ => null
        };

    /// <summary>A result's description for the dialog, cut at a word near 280 characters.</summary>
    private static string? ShortSummary(string? summary)
    {
        var text = summary?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length <= 280)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', 280);
        return text[..(cut > 200 ? cut : 280)].TrimEnd(' ', ',', ';', ':', '.') + "…";
    }

    /// <summary>
    /// The current state of catalog books the open Add book dialog shows, so in-flight requests
    /// move on (searching, downloading, importing, in library) without reopening it.
    /// </summary>
    public async Task<IActionResult> OnGetStatusAsync(string[]? ids, CancellationToken cancellationToken)
    {
        var catalogIds = (ids ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();
        var states = await new BookAddStateQuery(db, requestStore).GetAsync(
            catalogIds.Select(id => new BookAddLookup(id)).ToArray(),
            cancellationToken);
        return new JsonResult(new
        {
            states = catalogIds.ToDictionary(id => id, id => StateJson(states.GetValueOrDefault(id) ?? BookAddState.None))
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
            await requests.SubmitAsync(
                new AcquisitionRequestDraft(
                    MediaAcquisitionKind.Book,
                    BookCatalogService.CatalogRequestProvider,
                    catalogId.Trim(),
                    title.Trim(),
                    string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                    string.IsNullOrWhiteSpace(coverImageUrl) ? null : coverImageUrl.Trim(),
                    JsonSerializer.Serialize(new BookRequestPayload(catalogId.Trim(), title.Trim(), author?.Trim()), JsonSerializerOptions.Web)),
                cancellationToken);
        }
        catch (AcquisitionAccessDeniedException)
        {
            return Forbid();
        }

        var states = await new BookAddStateQuery(db, requestStore).GetAsync([new BookAddLookup(catalogId.Trim())], cancellationToken);
        return new JsonResult(StateJson(states.GetValueOrDefault(catalogId.Trim()) ?? BookAddState.None));
    }

    private static object StateJson(BookAddState state) => new
    {
        libraryWorkId = state.LibraryWorkId,
        requestStatus = state.RequestStatus,
        message = state.RequestMessage,
        progress = state.ProgressPercent,
        inFlight = state.IsInFlight
    };

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

    private SabnzbdSubmission BookSabnzbdSubmission(string name) =>
        new(
            CompletedDownloadImportService.ManualDownloadOperationKind,
            "SABnzbd download",
            name,
            account.ProfileId,
            SabnzbdPurpose.Books,
            JobName: name);
}

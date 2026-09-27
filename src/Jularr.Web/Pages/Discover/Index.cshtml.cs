using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Discovery;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Discover;

public sealed class IndexModel(
    AniListMetadataProvider animeProvider,
    NovelAniListProvider readingProvider,
    BookCatalogService books,
    AniListAccountService aniListAccount,
    AppDbContext db,
    NovelImportService novels,
    NovelMetadataService novelMetadata,
    CurrentAccountContext account,
    OperationRunner operations,
    ILogger<DiscoveryCoordinator> discoveryLogger) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public bool IsOwner => account.IsOwner;

    public async Task OnGetAsync()
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
    }

    public async Task<IActionResult> OnGetResultsAsync(
        string? q,
        string? category,
        string? mode,
        string? source,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var request = DiscoveryRequest.Parse(q, category, mode);
        var coordinator = new DiscoveryCoordinator(
            animeProvider,
            readingProvider,
            books,
            aniListAccount,
            db,
            discoveryLogger);

        var normalizedSource = source?.Trim().ToLowerInvariant();
        var includeAniList = normalizedSource is not "books";
        var includeBooks = normalizedSource is not "anilist";

        var result = await coordinator.GetAsync(
            request,
            account.ProfileId,
            account.IsOwner,
            includeAniList,
            includeBooks,
            cancellationToken);

        return new JsonResult(result);
    }

    public async Task<IActionResult> OnPostImportSourceAsync(
        string sourceUrl,
        string? metadataProvider,
        string? metadataExternalId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            var result = await operations.RunAsync(
                new OperationDescriptor(
                    "discover-novel-import",
                    "Novels",
                    "Import discovered novel",
                    string.IsNullOrWhiteSpace(metadataExternalId)
                        ? null
                        : $"AniList {metadataExternalId.Trim()}",
                    account.ProfileId,
                    OperationLane.Normal,
                    IsDownload: true,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        "Importing novel source.",
                        cancellationToken: token);

                    var workId = await novels.ImportWorkAsync(
                        sourceUrl,
                        token);

                    if (!string.IsNullOrWhiteSpace(metadataProvider) &&
                        !string.IsNullOrWhiteSpace(metadataExternalId))
                    {
                        try
                        {
                            await operation.ReportAsync(
                                80,
                                "Matching imported novel metadata.",
                                cancellationToken: token);

                            await novelMetadata.MatchAsync(
                                workId,
                                metadataProvider,
                                metadataExternalId,
                                token);

                            return (WorkId: workId, Status: Ui["discover.import.novelMatched"]);
                        }
                        catch (Exception exception) when (
                            exception is InvalidOperationException or
                            NovelMetadataProviderException)
                        {
                            await operation.LogAsync(
                                OperationLogLevel.Warning,
                                "NovelMetadata",
                                "Novel import completed, but metadata matching needs attention.",
                                CancellationToken.None);

                            return (
                                WorkId: workId,
                                Status: Ui.Format(
                                    "discover.import.novelMatchAttention",
                                    ("reason", exception.Message)));
                        }
                    }

                    return (
                        WorkId: workId,
                        Status: Ui["discover.import.novelImported"]);
                },
                "Discovered novel imported.",
                cancellationToken);

            TempData["Status"] = result.Status;
            return RedirectToPage("/Novels/Work", new { id = result.WorkId });
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }
}

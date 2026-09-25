using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Discovery;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Discover;

public sealed class IndexModel(
    AniListMetadataProvider animeProvider,
    NovelAniListProvider readingProvider,
    BookCatalogService books,
    AniListAccountService aniListAccount,
    AppDbContext db,
    NovelService novels,
    NovelMetadataService novelMetadata,
    CurrentAccountContext account,
    OperationRunner operations) : PageModel
{
    public bool IsOwner => account.IsOwner;

    public void OnGet()
    {
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
            db);

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

                            return (WorkId: workId, Status: "Novel imported and matched to AniList.");
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
                                Status: $"Novel imported. Metadata match needs attention: {exception.Message}");
                        }
                    }

                    return (
                        WorkId: workId,
                        Status: "Novel imported. Chapter text is loaded on demand.");
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

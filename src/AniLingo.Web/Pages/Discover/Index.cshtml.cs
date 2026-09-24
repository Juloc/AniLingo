using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Discovery;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Novels;
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
    CurrentAccountContext account) : PageModel
{
    public bool IsOwner => account.IsOwner;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetResultsAsync(
        string? q,
        string? category,
        string? mode,
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

        var result = await coordinator.GetAsync(
            request,
            account.ProfileId,
            account.IsOwner,
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
            var workId = await novels.ImportWorkAsync(
                sourceUrl,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(metadataProvider) &&
                !string.IsNullOrWhiteSpace(metadataExternalId))
            {
                try
                {
                    await novelMetadata.MatchAsync(
                        workId,
                        metadataProvider,
                        metadataExternalId,
                        cancellationToken);
                    TempData["Status"] =
                        "Novel imported and matched to AniList.";
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    NovelMetadataProviderException)
                {
                    TempData["Status"] =
                        $"Novel imported. Metadata match needs attention: {exception.Message}";
                }
            }
            else
            {
                TempData["Status"] =
                    "Novel imported. Chapter text is loaded on demand.";
            }

            return RedirectToPage("/Novels/Work", new { id = workId });
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }
}

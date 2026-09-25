using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Discovery;
using AniLingo.Web.Features.Manga;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Discover;

public sealed class MangaImportModel(
    AppDbContext db,
    CurrentAccountContext account,
    IHttpClientFactory httpClientFactory) : PageModel
{
    public string AniListId { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string AniListUrl => $"https://anilist.co/manga/{AniListId}";

    public IActionResult OnGet(
        string? anilistId,
        string? title)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (!TryNormalizeSelection(
                anilistId,
                title,
                out var normalizedId,
                out var normalizedTitle))
        {
            return BadRequest("A valid AniList manga ID is required.");
        }

        AniListId = normalizedId;
        Title = normalizedTitle;
        return Page();
    }

    [RequestSizeLimit(4L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 4L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> OnPostUploadAsync(
        string? anilistId,
        string? title,
        string? seriesTitle,
        IFormFile[]? archives,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (!TryNormalizeSelection(
                anilistId,
                title,
                out var normalizedId,
                out var normalizedTitle))
        {
            return BadRequest("A valid AniList manga ID is required.");
        }

        try
        {
            var upload = new MangaUploadService();
            var sourcePath = await upload.SaveSeriesAsync(
                string.IsNullOrWhiteSpace(seriesTitle)
                    ? normalizedTitle
                    : seriesTitle,
                archives ?? [],
                cancellationToken);

            return await ImportAndMatchAsync(
                sourcePath,
                normalizedId,
                normalizedTitle,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage(new
            {
                anilistId = normalizedId,
                title = normalizedTitle
            });
        }
    }

    public async Task<IActionResult> OnPostImportPathAsync(
        string? anilistId,
        string? title,
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (!TryNormalizeSelection(
                anilistId,
                title,
                out var normalizedId,
                out var normalizedTitle))
        {
            return BadRequest("A valid AniList manga ID is required.");
        }

        try
        {
            return await ImportAndMatchAsync(
                sourcePath ?? "",
                normalizedId,
                normalizedTitle,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage(new
            {
                anilistId = normalizedId,
                title = normalizedTitle
            });
        }
    }

    private async Task<IActionResult> ImportAndMatchAsync(
        string sourcePath,
        string aniListId,
        string title,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        var importer = new MangaImportService(repository);
        var result = await importer.ImportAsync(
            sourcePath,
            cancellationToken);

        var metadata = new MangaAniListService(
            repository,
            httpClientFactory);

        try
        {
            await metadata.MatchAsync(
                result.SeriesId,
                aniListId,
                cancellationToken);

            TempData["Status"] =
                $"Imported {result.ChapterCount} chapter(s) / {result.PageCount} page(s) and matched {title} to AniList.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] =
                $"Manga imported, but the AniList match could not be completed: {exception.Message}";
        }

        DiscoveryCoordinator.InvalidateCache();

        return RedirectToPage(
            "/Manga/Series",
            new { id = result.SeriesId });
    }

    public static bool TryNormalizeSelection(
        string? anilistId,
        string? title,
        out string normalizedId,
        out string normalizedTitle)
    {
        normalizedId = "";
        normalizedTitle = "";

        if (!int.TryParse(anilistId, out var id) || id <= 0)
        {
            return false;
        }

        normalizedId = id.ToString();
        normalizedTitle = title?.Trim() ?? "";
        if (normalizedTitle.Length == 0)
        {
            normalizedTitle = $"AniList manga {normalizedId}";
        }
        else if (normalizedTitle.Length > 200)
        {
            normalizedTitle = normalizedTitle[..200];
        }

        return true;
    }
}

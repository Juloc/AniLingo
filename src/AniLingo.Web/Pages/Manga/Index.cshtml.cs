using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Manga;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Manga;

public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext account) : PageModel
{
    public IReadOnlyList<MangaSeriesItem> Series { get; private set; } = [];
    public IReadOnlyList<MangaSeriesItem> ContinueReading { get; private set; } = [];
    public bool IsOwner => account.IsOwner;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        Series = await repository.GetLibraryAsync(
            account.ProfileId,
            cancellationToken);
        ContinueReading = Series
            .Where(x => x.HasProgress)
            .OrderByDescending(x => x.LastReadAt)
            .Take(8)
            .ToArray();
    }


    [RequestSizeLimit(4L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 4L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> OnPostUploadAsync(
        string? seriesTitle,
        IFormFile[]? archives,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            var repository = new MangaRepository(db);
            var upload = new MangaUploadService();
            var sourcePath = await upload.SaveSeriesAsync(
                seriesTitle,
                archives ?? [],
                cancellationToken);
            var importer = new MangaImportService(repository);
            var result = await importer.ImportAsync(
                sourcePath,
                cancellationToken);

            TempData["Status"] =
                $"Imported {result.ChapterCount} chapter(s) / {result.PageCount} page(s).";
            return RedirectToPage(
                "/Manga/Series",
                new { id = result.SeriesId });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostImportAsync(
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        try
        {
            var repository = new MangaRepository(db);
            var importer = new MangaImportService(repository);
            var result = await importer.ImportAsync(
                sourcePath ?? "",
                cancellationToken);

            TempData["Status"] =
                $"Imported {result.ChapterCount} chapter(s) / {result.PageCount} page(s).";
            return RedirectToPage(
                "/Manga/Series",
                new { id = result.SeriesId });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }
}

using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Manga;
using AniLingo.Web.Features.MediaMapping;
using AniLingo.Web.Features.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Manga;

[MangaUploadRequestLimits("Upload")]
public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext account,
    OperationRunner operations,
    IHttpClientFactory httpClientFactory,
    MediaMappingReviewStore mappingReviewStore) : PageModel
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
            var result = await operations.RunAsync(
                new OperationDescriptor(
                    "manga-upload-import",
                    "Manga",
                    "Import uploaded Manga",
                    string.IsNullOrWhiteSpace(seriesTitle) ? null : seriesTitle.Trim(),
                    account.ProfileId,
                    OperationLane.Normal,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        5,
                        "Validating uploaded Manga archives.",
                        cancellationToken: token);

                    var repository = new MangaRepository(db);
                    var upload = new MangaUploadService();
                    var sourcePath = await upload.SaveSeriesAsync(
                        seriesTitle,
                        archives ?? [],
                        token);

                    await operation.ReportAsync(
                        35,
                        "Importing Manga chapters and pages.",
                        cancellationToken: token);

                    var importer = new MangaImportService(repository);
                    var imported = await importer.ImportAsync(
                        sourcePath,
                        token);
                    var metadata = new MangaAniListService(
                        repository,
                        httpClientFactory,
                        mappingReviewStore);
                    await metadata.AutoMatchAsync(
                        imported.SeriesId,
                        token);
                    return imported;
                },
                "Manga upload imported.",
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
            var result = await operations.RunAsync(
                new OperationDescriptor(
                    "manga-path-import",
                    "Manga",
                    "Import mounted Manga source",
                    ProfileId: account.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        "Scanning mounted Manga source.",
                        cancellationToken: token);

                    var repository = new MangaRepository(db);
                    var importer = new MangaImportService(repository);
                    var imported = await importer.ImportAsync(
                        sourcePath ?? "",
                        token);
                    var metadata = new MangaAniListService(
                        repository,
                        httpClientFactory,
                        mappingReviewStore);
                    await metadata.AutoMatchAsync(
                        imported.SeriesId,
                        token);
                    return imported;
                },
                "Mounted Manga source imported.",
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

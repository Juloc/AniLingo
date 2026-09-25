using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Manga;
using AniLingo.Web.Features.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Manga;

public sealed class SeriesModel(
    AppDbContext db,
    CurrentAccountContext account,
    IHttpClientFactory httpClientFactory,
    OperationRunner operations) : PageModel
{
    public MangaSeriesDetail Series { get; private set; } = null!;
    public MangaProgressItem? Progress { get; private set; }
    public IReadOnlyList<MangaAniListCandidate> SearchResults { get; private set; } = [];
    public string Query { get; private set; } = "";
    public bool IsOwner => account.IsOwner;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? q,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        var series = await repository.GetSeriesAsync(id, cancellationToken);
        if (series is null)
        {
            return NotFound();
        }

        Series = series;
        Progress = await repository.GetProgressAsync(
            account.ProfileId,
            id,
            cancellationToken);

        Query = q?.Trim() ?? "";
        if (account.IsOwner && Query.Length > 0)
        {
            var metadata = new MangaAniListService(repository, httpClientFactory);
            SearchResults = await metadata.SearchAsync(
                Query,
                cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostMatchAsync(
        Guid id,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        await operations.RunAsync(
            new OperationDescriptor(
                "manga-anilist-match",
                "Manga",
                "Match Manga metadata",
                ProfileId: account.ProfileId,
                Lane: OperationLane.Normal,
                Retryable: false),
            async (_, token) =>
            {
                var repository = new MangaRepository(db);
                var metadata = new MangaAniListService(repository, httpClientFactory);
                await metadata.MatchAsync(id, externalId, token);
            },
            "Manga metadata matched.",
            cancellationToken);

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var repository = new MangaRepository(db);
        var series = await repository.GetSeriesAsync(id, cancellationToken);
        if (series is null)
        {
            return NotFound();
        }

        var result = await operations.RunAsync(
            new OperationDescriptor(
                "manga-refresh",
                "Manga",
                "Refresh Manga source",
                series.Title,
                account.ProfileId,
                OperationLane.Normal,
                Retryable: false),
            async (operation, token) =>
            {
                await operation.ReportAsync(
                    10,
                    "Rescanning Manga chapters and pages.",
                    cancellationToken: token);

                var importer = new MangaImportService(repository);
                var imported = await importer.ImportAsync(
                    series.SourcePath,
                    token);
                var metadata = new MangaAniListService(
                    repository,
                    httpClientFactory);
                await metadata.AutoMatchAsync(
                    imported.SeriesId,
                    token);
                return imported;
            },
            "Manga source refreshed.",
            cancellationToken);

        TempData["Status"] =
            $"Refreshed {result.ChapterCount} chapter(s) / {result.PageCount} page(s).";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDirectionAsync(
        Guid id,
        string? direction,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var repository = new MangaRepository(db);
        await repository.SetDirectionAsync(
            id,
            direction ?? "rtl",
            cancellationToken);
        return RedirectToPage(new { id });
    }
}

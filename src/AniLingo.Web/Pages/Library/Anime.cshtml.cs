using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed class AnimeModel(
    AppDbContext db,
    AnimeMetadataService metadataService) : PageModel
{
    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public string LocalAnimeTitle { get; private set; } = "";
    public AnimeMetadata? Metadata { get; private set; }
    public string SearchQuery { get; private set; } = "";
    public string? MetadataError { get; private set; }
    public IReadOnlyList<AnimeMetadataCandidate> SearchResults { get; private set; } = [];
    public IReadOnlyList<EpisodeRow> Episodes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? q,
        CancellationToken cancellationToken)
    {
        var anime = await db.Anime
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (anime is null)
        {
            return NotFound();
        }

        AnimeId = anime.Id;
        LocalAnimeTitle = anime.Title;
        Metadata = await metadataService.GetAsync(id, cancellationToken);
        AnimeTitle = Metadata?.PreferredTitle ?? anime.Title;
        SearchQuery = string.IsNullOrWhiteSpace(q) ? anime.Title : q.Trim();

        if (TempData.TryGetValue("MetadataError", out var metadataError))
        {
            MetadataError = metadataError?.ToString();
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            try
            {
                SearchResults = await metadataService.SearchAsync(
                    AniListMetadataProvider.ProviderKey,
                    SearchQuery,
                    8,
                    cancellationToken);
            }
            catch (MetadataProviderException exception)
            {
                MetadataError = exception.Message;
            }
        }

        var episodeRows = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == id)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .Select(episode => new
            {
                episode.Id,
                episode.SeasonNumber,
                episode.Number,
                episode.Title,
                JapaneseSubtitleTracks = db.SubtitleTracks.Count(
                    x => x.EpisodeId == episode.Id && x.Language == "ja")
            })
            .ToListAsync(cancellationToken);

        var episodeIds = episodeRows.Select(x => x.Id).ToArray();
        List<CoverageRow> coverageRows;

        if (episodeIds.Length == 0)
        {
            coverageRows = [];
        }
        else
        {
            coverageRows = await (
                from episodeTerm in db.EpisodeTerms.AsNoTracking()
                join userTermValue in db.UserTerms.AsNoTracking()
                        .Where(x => x.ProfileId == LearningProfile.DefaultId)
                    on episodeTerm.TermId equals userTermValue.TermId into userTerms
                from userTerm in userTerms.DefaultIfEmpty()
                where episodeIds.Contains(episodeTerm.EpisodeId)
                select new CoverageRow(
                    episodeTerm.EpisodeId,
                    episodeTerm.Occurrences,
                    userTerm == null ? null : userTerm.State))
                .ToListAsync(cancellationToken);
        }

        var coverageByEpisode = coverageRows
            .GroupBy(x => x.EpisodeId)
            .ToDictionary(
                group => group.Key,
                group => new Coverage(
                    group.Count(),
                    group.Sum(x => x.Occurrences),
                    group.Where(x => x.State is UserTermState.Known or UserTermState.Learning)
                        .Sum(x => x.Occurrences)));

        Episodes = episodeRows
            .Select(episode =>
            {
                var coverage = coverageByEpisode.GetValueOrDefault(episode.Id, Coverage.Empty);

                return new EpisodeRow(
                    episode.Id,
                    episode.SeasonNumber,
                    episode.Number,
                    episode.Title,
                    coverage.TotalTerms,
                    coverage.TotalOccurrences,
                    coverage.PreparedOccurrences,
                    episode.JapaneseSubtitleTracks);
            })
            .ToArray();

        return Page();
    }

    public async Task<IActionResult> OnPostMatchMetadataAsync(
        Guid id,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await metadataService.MatchAsync(
                id,
                provider,
                externalId,
                cancellationToken);

            if (!result.Success)
            {
                TempData["MetadataError"] = result.Error;
            }
        }
        catch (MetadataProviderException exception)
        {
            TempData["MetadataError"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshMetadataAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await metadataService.RefreshAsync(id, cancellationToken))
            {
                TempData["MetadataError"] = "Metadata could not be refreshed.";
            }
        }
        catch (MetadataProviderException exception)
        {
            TempData["MetadataError"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveMetadataAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await metadataService.RemoveAsync(id, cancellationToken);
        return RedirectToPage(new { id });
    }

    private sealed record CoverageRow(
        Guid EpisodeId,
        int Occurrences,
        UserTermState? State);

    private sealed record Coverage(
        int TotalTerms,
        int TotalOccurrences,
        int PreparedOccurrences)
    {
        public static Coverage Empty { get; } = new(0, 0, 0);
    }

    public sealed record EpisodeRow(
        Guid Id,
        int SeasonNumber,
        int Number,
        string Title,
        int TotalTerms,
        int TotalOccurrences,
        int PreparedOccurrences,
        int JapaneseSubtitleTracks)
    {
        public int PreparationPercent => TotalOccurrences == 0
            ? 0
            : (int)Math.Floor((double)PreparedOccurrences / TotalOccurrences * 100);
    }
}

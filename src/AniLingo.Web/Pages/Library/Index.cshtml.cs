using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<AnimeRow> Anime { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from anime in db.Anime.AsNoTracking()
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            orderby metadata == null ? anime.Title : metadata.PreferredTitle
            select new AnimeRow(
                anime.Id,
                metadata == null ? anime.Title : metadata.PreferredTitle,
                anime.Title,
                db.Episodes.Count(episode => episode.AnimeId == anime.Id),
                metadata == null ? null : metadata.CoverImageUrl))
            .ToListAsync(cancellationToken);

        Anime = rows
            .Select(row => row with
            {
                CoverImageUrl = AnimeArtworkStore.ResolvePosterUrl(
                    row.Id,
                    row.CoverImageUrl)
            })
            .ToArray();
    }

    public sealed record AnimeRow(
        Guid Id,
        string Title,
        string LocalTitle,
        int EpisodeCount,
        string? CoverImageUrl);
}

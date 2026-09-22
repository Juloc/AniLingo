using AniLingo.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<AnimeRow> Anime { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Anime = await db.Anime
            .AsNoTracking()
            .OrderBy(x => x.Title)
            .Select(anime => new AnimeRow(
                anime.Id,
                anime.Title,
                db.Episodes.Count(episode => episode.AnimeId == anime.Id)))
            .ToListAsync(cancellationToken);
    }

    public sealed record AnimeRow(Guid Id, string Title, int EpisodeCount);
}

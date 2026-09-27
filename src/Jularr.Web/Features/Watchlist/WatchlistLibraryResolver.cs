using Jularr.Web.Data;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Manga;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Tracking;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Watchlist;

public sealed record WatchlistLibraryMatch(Guid MediaId, string DetailsUrl);

/// <summary>
/// Decides at read time whether a followed work is in the library, from the library's own
/// provider matches (anime metadata and episode mappings, manga series, light novels). Nothing
/// about the library is stored with a follow, so adding or removing a library entry takes effect
/// immediately.
/// </summary>
public sealed class WatchlistLibraryResolver(AppDbContext db, AniListAccountStore? mappings = null)
{
    public async Task<IReadOnlyDictionary<string, WatchlistLibraryMatch>> ResolveAsync(
        IEnumerable<WatchlistIdentity> identities,
        CancellationToken cancellationToken)
    {
        var aniList = identities
            .Where(identity => identity.ProviderKey == AniListMetadataProvider.ProviderKey)
            .DistinctBy(identity => identity.Key)
            .ToArray();
        var result = new Dictionary<string, WatchlistLibraryMatch>(StringComparer.Ordinal);
        if (aniList.Length == 0)
        {
            return result;
        }

        var animeIds = IdsOf(aniList, WatchlistMediaType.Anime);
        if (animeIds.Length > 0)
        {
            var matches = await db.AnimeMetadata
                .AsNoTracking()
                .Where(item => item.Provider == AniListMetadataProvider.ProviderKey && animeIds.Contains(item.ExternalId))
                .Select(item => new { item.ExternalId, item.AnimeId })
                .ToListAsync(cancellationToken);
            foreach (var match in matches)
            {
                Add(result, WatchlistMediaType.Anime, match.ExternalId, match.AnimeId, $"/Library/Anime/{match.AnimeId}");
            }

            if (mappings is not null)
            {
                foreach (var mapping in await mappings.LoadAllEpisodeMappingsAsync(cancellationToken))
                {
                    if (mapping.Provider.Equals(AniListMetadataProvider.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                        animeIds.Contains(mapping.ExternalId))
                    {
                        Add(result, WatchlistMediaType.Anime, mapping.ExternalId, mapping.AnimeId, $"/Library/Anime/{mapping.AnimeId}");
                    }
                }
            }
        }

        var novelIds = IdsOf(aniList, WatchlistMediaType.LightNovel);
        if (novelIds.Length > 0)
        {
            var works = await db.NovelWorks
                .AsNoTracking()
                .Where(work =>
                    work.MetadataProvider == AniListMetadataProvider.ProviderKey &&
                    work.MetadataExternalId != null &&
                    novelIds.Contains(work.MetadataExternalId) &&
                    work.SourceProvider != BookCatalogService.ImportedBookProvider)
                .Select(work => new { ExternalId = work.MetadataExternalId!, work.Id })
                .ToListAsync(cancellationToken);
            foreach (var work in works)
            {
                Add(result, WatchlistMediaType.LightNovel, work.ExternalId, work.Id, $"/Novels/Work/{work.Id}");
            }
        }

        var mangaIds = IdsOf(aniList, WatchlistMediaType.Manga);
        if (mangaIds.Length > 0)
        {
            foreach (var (externalId, seriesId) in await new MangaRepository(db).GetAniListMatchesAsync(mangaIds, cancellationToken))
            {
                Add(result, WatchlistMediaType.Manga, externalId, seriesId, $"/Manga/Series/{seriesId}");
            }
        }

        return result;
    }

    /// <summary>The items with their library entry and link resolved now.</summary>
    public async Task<IReadOnlyList<WatchlistItem>> ApplyAsync(
        IReadOnlyList<WatchlistItem> items,
        CancellationToken cancellationToken)
    {
        var matches = await ResolveAsync(items.Select(item => item.Identity), cancellationToken);
        return items
            .Select(item => matches.TryGetValue(item.Identity.Key, out var match)
                ? item with { LocalMediaId = match.MediaId, DetailsUrl = match.DetailsUrl }
                : item with { LocalMediaId = null, DetailsUrl = item.Identity.ProviderUrl })
            .ToArray();
    }

    private static string[] IdsOf(IEnumerable<WatchlistIdentity> identities, WatchlistMediaType type) =>
        identities
            .Where(identity => identity.MediaType == type)
            .Select(identity => identity.ExternalKey)
            .ToArray();

    private static void Add(
        Dictionary<string, WatchlistLibraryMatch> result,
        WatchlistMediaType type,
        string externalId,
        Guid mediaId,
        string url) =>
        result.TryAdd(
            new WatchlistIdentity(type, AniListMetadataProvider.ProviderKey, externalId).Key,
            new WatchlistLibraryMatch(mediaId, url));
}

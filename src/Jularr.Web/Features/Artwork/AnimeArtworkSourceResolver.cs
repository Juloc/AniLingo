using Jularr.Web.Data;
using Jularr.Web.Features.Mapping;

namespace Jularr.Web.Features.Artwork;

/// <summary>
/// Picks which provider's stored images (<see cref="Metadata.AnimeMetadata.CoverImageUrl"/>/
/// <c>BannerImageUrl</c>) an anime's provider artwork comes from (issue #568). Jularr only ever
/// stores one provider's images on <c>AnimeMetadata</c> today (whichever provider filled the
/// DisplayMetadata role when the anime was matched), so this only ever selects between "use them"
/// and "use nothing": images are used exactly when the resolved Artwork role for the anime names
/// that same provider. With nothing configured the Artwork role's built-in default is AniList,
/// the same provider DisplayMetadata defaults to, so an unmatched or freshly matched anime keeps
/// today's behaviour exactly. A work whose Artwork role was explicitly assigned to a different
/// provider stops picking up artwork sourced from the wrong one; this never touches the write
/// path (<see cref="BesideMediaArtworkStore"/>) itself, only which URLs are handed to it.
/// </summary>
public static class AnimeArtworkSourceResolver
{
    public static AnimeProviderArtwork? Resolve(
        string metadataProvider,
        string? coverImageUrl,
        string? bannerImageUrl,
        string resolvedArtworkProvider) =>
        string.Equals(metadataProvider, resolvedArtworkProvider, StringComparison.OrdinalIgnoreCase)
            ? new AnimeProviderArtwork(coverImageUrl, bannerImageUrl)
            : null;

    /// <summary>Resolves the Artwork role for <paramref name="animeId"/> and applies <see cref="Resolve"/>.</summary>
    public static async Task<AnimeProviderArtwork?> ResolveForAnimeAsync(
        AppDbContext db,
        Guid animeId,
        string metadataProvider,
        string? coverImageUrl,
        string? bannerImageUrl,
        CancellationToken cancellationToken)
    {
        var role = await new ProviderRoleAssignmentStore(db).ResolveRoleForWorkAsync(
            animeId,
            MappingProviderRole.Artwork,
            cancellationToken);

        return Resolve(metadataProvider, coverImageUrl, bannerImageUrl, role.Provider);
    }
}

namespace Jularr.Web.Ui;

/// <summary>
/// A rendered shelf of <see cref="MediaBannerCardModel"/> cards: one horizontal row with a heading and
/// an optional "see all" deep link. Built once by a page/service from a discovery board and rendered by
/// <c>_MediaShelf</c>. Shared so the later shelf surfaces (#427/#428/#434) render rows identically.
/// </summary>
/// <param name="Id">Stable slug for the section (also used as its accessible id).</param>
/// <param name="Heading">Fully-resolved, localized heading (e.g. "Trending · Anime").</param>
/// <param name="DeepLinkUrl">Where the "see all" link goes, or <c>null</c> for no link.</param>
/// <param name="SeeAllLabel">Localized label for the "see all" link; required when <paramref name="DeepLinkUrl"/> is set.</param>
public sealed record MediaShelfModel(
    string Id,
    string Heading,
    string? DeepLinkUrl,
    string? SeeAllLabel,
    IReadOnlyList<MediaBannerCardModel> Cards);

/// <summary>A board of shelves rendered by <c>_MediaShelfBoard</c>.</summary>
public sealed record MediaShelfBoardModel(IReadOnlyList<MediaShelfModel> Shelves)
{
    public bool HasShelves => Shelves.Count > 0;
}

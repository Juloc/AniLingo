using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Ui;

namespace Jularr.Web.Features.Recommendations;

/// <summary>
/// Renders the media-neutral recommendation shelves (#428) onto #595's shared shelf surface: each
/// <see cref="MediaRecommendationShelf"/> becomes a <see cref="MediaShelfModel"/> of shared
/// <see cref="MediaBannerCardModel"/> cards, with an explainable, localized heading built from the
/// shelf's seed. The shared card model is reused untouched. Both the standalone "For you" page and the
/// Discover landing feed personalized rows through this one mapper so they render identically.
/// </summary>
public static class MediaRecommendationShelfView
{
    public static MediaShelfBoardModel ToBoard(MediaRecommendationResult result, UiTextBundle ui) =>
        new(ToShelves(result, ui));

    public static IReadOnlyList<MediaShelfModel> ToShelves(MediaRecommendationResult result, UiTextBundle ui) =>
        result.Shelves
            .Select(shelf => ToShelf(shelf, ui))
            .ToArray();

    private static MediaShelfModel ToShelf(MediaRecommendationShelf shelf, UiTextBundle ui) =>
        new(
            shelf.Id,
            Heading(shelf, ui),
            // Personalized rows have no "browse all" grid to deep-link to; the row is the whole answer.
            null,
            null,
            shelf.Items
                .Select(item => MediaBannerCardModel.Create(ToCardData(item), ui))
                .ToArray());

    private static string Heading(MediaRecommendationShelf shelf, UiTextBundle ui)
    {
        var title = shelf.SeedTitle ?? "";
        return shelf.Kind switch
        {
            MediaRecommendationShelfKind.Continuation =>
                ui.Format("recommendations.shelf.continue", ("title", title)),
            _ => ui.Format("recommendations.shelf.becauseYou", ("title", title))
        };
    }

    private static MediaBannerCardData ToCardData(MediaRecommendationItem item)
    {
        var candidate = item.Candidate;
        var kind = candidate.MediaType switch
        {
            WorkMediaType.Anime => MediaBannerKind.Anime,
            WorkMediaType.Manga => MediaBannerKind.Manga,
            WorkMediaType.LightNovel => MediaBannerKind.LightNovel,
            _ => MediaBannerKind.Book
        };

        return new MediaBannerCardData(
            kind,
            candidate.Title,
            candidate.Href,
            BackdropUrl: candidate.CoverImageUrl,
            Year: candidate.Year);
    }
}

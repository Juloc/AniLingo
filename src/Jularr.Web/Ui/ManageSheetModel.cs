using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Html;

namespace Jularr.Web.Ui;

/// <summary>
/// One labelled group of owner-only actions inside a <c>_ManageSheet</c> (e.g. "Metadata",
/// "Mapping", "Files", "Acquisition"). <paramref name="Content"/> is the page's own Razor
/// markup for that group (forms, links, panels), captured as a Razor template so the shared
/// sheet never needs to know what a page's owner controls look like.
/// </summary>
public sealed record ManageSheetGroup(string Heading, IHtmlContent Content);

/// <summary>
/// A small "Needs review" indicator shown on the sheet's trigger button instead of as a card
/// in the consumer layout (#519): a dot/short link, never full mapping-state details.
/// </summary>
public sealed record ManageSheetReviewBadge(string Text, string Url);

/// <summary>
/// View model of the shared owner-only <c>_ManageSheet</c> partial (#519, part of epic #510).
/// Every Library/Anime, Library/Episode, Books/Details, Manga/Series, Novels/Work and
/// Franchises/Details owner-only control renders inside one of these on its page instead of
/// mixed into the consumer media layout. The caller only renders this when
/// <c>CurrentAccountContext.IsOwner</c> is true.
/// </summary>
public sealed record ManageSheetModel(
    string Id,
    string TriggerLabel,
    string Title,
    IReadOnlyList<ManageSheetGroup> Groups,
    UiTextBundle Ui,
    ManageSheetReviewBadge? ReviewBadge = null,
    bool OpenOnLoad = false)
{
    public static ManageSheetGroup? Group(string heading, IHtmlContent? content) =>
        content is null ? null : new ManageSheetGroup(heading, content);
}

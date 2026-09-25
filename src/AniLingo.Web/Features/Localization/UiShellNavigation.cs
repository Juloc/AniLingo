namespace AniLingo.Web.Features.Localization;

public sealed record UiNavigationItem(
    string Id,
    string LabelKey,
    string Href,
    string Icon,
    bool IsActive);

/// <summary>
/// Canonical destination model for the shared app shell. The desktop sidebar
/// renders <see cref="Primary"/> and <see cref="Secondary"/>; the mobile bottom
/// bar renders a bounded <see cref="MobilePrimary"/> set plus a More menu with
/// every remaining destination, so both layouts expose the same destinations.
/// Labels are UI catalog keys, never literal text.
/// </summary>
public sealed record UiShellNavigation(
    IReadOnlyList<UiNavigationItem> Primary,
    IReadOnlyList<UiNavigationItem> Secondary,
    IReadOnlyList<UiNavigationItem> MobilePrimary,
    IReadOnlyList<UiNavigationItem> MobileMore)
{
    public const int MaxMobilePrimaryItems = 4;

    public bool MoreIsActive => MobileMore.Any(x => x.IsActive);

    public static UiShellNavigation Build(
        PathString path,
        bool learningVisible,
        bool isOwner)
    {
        var home = Item("home", "nav.home", "/", "home", !path.HasValue || path == "/");
        var discover = Item("discover", "nav.discover", "/Discover", "discover", IsUnder(path, "/Discover"));
        var library = Item("library", "nav.library", "/Library", "library", IsUnder(path, "/Library"));
        var reading = Item("reading", "nav.reading", "/Reading", "reading", IsUnder(path, "/Reading", "/Novels", "/Manga"));
        var books = Item("books", "nav.books", "/Books", "books", IsUnder(path, "/Books"));
        var learn = learningVisible
            ? Item("learn", "nav.learn", "/Learn", "learn", IsUnder(path, "/Learn", "/Statistics", "/Kana"))
            : null;
        var settings = Item("settings", "nav.settings", "/Settings", "settings", IsUnder(path, "/Settings"));
        var admin = isOwner
            ? Item("admin", "nav.admin", "/Admin", "admin", IsUnder(path, "/Admin"))
            : null;

        var primary = Present(home, discover, library, reading, books, learn);
        var secondary = Present(settings, admin);

        // High-frequency destinations stay one tap away on phones. Discover
        // takes the Learning slot when Learning is off for this profile.
        var mobilePrimary = Present(home, library, reading, learn ?? discover);
        var mobileMore = primary
            .Concat(secondary)
            .Where(x => !mobilePrimary.Contains(x))
            .ToArray();

        return new UiShellNavigation(primary, secondary, mobilePrimary, mobileMore);
    }

    private static UiNavigationItem Item(
        string id,
        string labelKey,
        string href,
        string icon,
        bool isActive) =>
        new(id, labelKey, href, icon, isActive);

    private static bool IsUnder(PathString path, params string[] roots) =>
        roots.Any(root => path.StartsWithSegments(root, StringComparison.OrdinalIgnoreCase));

    private static UiNavigationItem[] Present(params UiNavigationItem?[] items) =>
        items.OfType<UiNavigationItem>().ToArray();
}

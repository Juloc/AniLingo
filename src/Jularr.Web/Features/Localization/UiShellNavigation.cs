namespace Jularr.Web.Features.Localization;

public sealed record UiNavigationItem(
    string Id,
    string LabelKey,
    string Href,
    string Icon,
    bool IsActive);

/// <summary>A titled group of links inside a sidebar context (Admin or Settings).</summary>
public sealed record UiNavigationGroup(string TitleKey, IReadOnlyList<UiNavigationItem> Items);

/// <summary>
/// The sidebar while the user is inside an area with its own pages (Admin, Settings): a way back
/// to the app, the area title and its pages, grouped. Replaces the app destinations in the
/// desktop sidebar; the mobile bottom bar keeps the app destinations.
/// </summary>
public sealed record UiNavigationContext(
    string Id,
    string TitleKey,
    UiNavigationItem Back,
    IReadOnlyList<UiNavigationGroup> Groups);

/// <summary>
/// One destination in <see cref="UiNavigationCatalog"/>. <see cref="Matches"/> are the path roots
/// that mark it active (the href's path when empty); <see cref="Exact"/> matches the href only.
/// </summary>
public sealed record UiNavigationEntry(
    string Id,
    string LabelKey,
    string Href,
    string Icon,
    string[]? Matches = null,
    bool Exact = false,
    bool OwnerOnly = false,
    bool RequiresLearning = false);

public sealed record UiNavigationSection(string TitleKey, UiNavigationEntry[] Entries);

/// <summary>
/// Every sidebar destination in one place. To move, rename, regroup or hide a link, edit this
/// table only: pages, sidebar, mobile bar and tests all read from it. Each page appears once.
/// </summary>
public static class UiNavigationCatalog
{
    public static readonly UiNavigationEntry[] App =
    [
        new("home", "nav.home", "/", "home", Exact: true),
        new("discover", "nav.discover", "/Discover", "discover"),
        new("library", "nav.library", "/Library", "library"),
        new("reading", "nav.reading", "/Reading", "reading", ["/Reading", "/Novels", "/Manga"]),
        new("books", "nav.books", "/Books", "books"),
        new("learn", "nav.learn", "/Learn", "learn", ["/Learn", "/Statistics", "/Kana"], RequiresLearning: true)
    ];

    public static readonly UiNavigationEntry[] Secondary =
    [
        new("settings", "nav.settings", "/Settings", "settings"),
        new("admin", "nav.admin", "/Admin", "admin", OwnerOnly: true)
    ];

    /// <summary>Phone bottom bar, in order; the first id that is present wins each slot.</summary>
    public static readonly string[][] MobilePrimarySlots =
    [
        ["home"],
        ["library"],
        ["reading"],
        ["learn", "discover"]
    ];

    public static readonly UiNavigationSection[] Admin =
    [
        new("nav.group.adminPeople",
        [
            new("admin-overview", "admin.nav.overview", "/Admin", "admin", Exact: true),
            new("admin-users", "admin.nav.users", "/Admin/Users", "users", ["/Admin/Users", "/Admin/User"]),
            new("admin-requests", "admin.nav.requests", "/Admin/Requests", "requests")
        ]),
        new("nav.group.adminMedia",
        [
            new("admin-usenet", "admin.nav.usenet", "/Admin/Usenet", "download", ["/Admin/Usenet", "/Settings/Indexers", "/Settings/DownloadClients"]),
            new("admin-anime-acquisition", "admin.nav.animeAcquisition", "/Acquisition", "library", ["/Acquisition"]),
            new("admin-import", "admin.nav.importSettings", "/Settings/Acquisition", "folder", ["/Settings/Acquisition", "/Settings/Naming"]),
            new("admin-mapping", "admin.nav.mapping", "/Settings/MappingReview", "link", ["/Settings/MappingReview", "/Settings/MappingSegments"]),
            new("admin-subtitles", "admin.nav.subtitles", "/Admin/Subtitles", "subtitles", ["/Admin/Subtitles", "/Settings/Subtitles"]),
            new("admin-sonarr", "admin.nav.sonarr", "/Admin/Sonarr", "sync", ["/Admin/Sonarr", "/Settings/Sonarr", "/Settings/SonarrMigration"])
        ]),
        new("nav.group.adminSystem",
        [
            new("admin-operations", "admin.nav.operations", "/Admin/Operations", "activity", ["/Admin/Operations", "/Admin/Operation"]),
            new("admin-scans", "admin.nav.scans", "/Admin/Scans", "scan"),
            new("admin-logs", "admin.nav.logs", "/Admin/Logs", "logs"),
            new("admin-ai", "admin.nav.ai", "/Admin/Ai", "spark"),
            new("admin-localization", "admin.nav.localization", "/LocalizationAdmin", "globe"),
            new("admin-api-keys", "admin.nav.apiKeys", "/Settings/ApiKeys", "key"),
            new("admin-system", "admin.nav.system", "/Admin/System", "server")
        ])
    ];

    public static readonly UiNavigationSection[] Settings =
    [
        new("nav.group.settingsYou",
        [
            new("settings-overview", "settings.nav.overview", "/Settings", "settings", Exact: true),
            new("settings-account", "settings.nav.account", "/Settings/User", "users"),
            new("settings-appearance", "settings.nav.appearance", "/Settings/Appearance", "palette", ["/Settings/Appearance", "/Appearance"]),
            new("settings-language", "settings.nav.language", "/Settings/Language", "globe", ["/Settings/Language", "/LocalizationPreferences"])
        ]),
        new("nav.group.settingsLearning",
        [
            new("settings-learning", "settings.nav.learning", "/Settings/Learning", "learn", ["/Settings/Learning", "/Settings/LearningCourses", "/Settings/LearningScope"]),
            new("settings-ai", "settings.nav.ai", "/Settings/Ai", "spark")
        ]),
        new("nav.group.settingsConnections",
        [
            new("settings-anilist", "settings.nav.anilist", "/Settings/AniList", "sync"),
            new("settings-offline", "settings.nav.offline", "/Settings/Offline", "download")
        ])
    ];

    /// <summary>Every path root of a section, used to decide which sidebar context a page belongs to.</summary>
    public static IEnumerable<string> Roots(UiNavigationSection[] sections) =>
        sections.SelectMany(section => section.Entries).SelectMany(entry => entry.Matches ?? [PathOf(entry.Href)]);

    public static string PathOf(string href)
    {
        var cut = href.IndexOfAny(['?', '#']);
        return cut < 0 ? href : href[..cut];
    }
}

/// <summary>
/// Canonical destination model for the shared app shell, built from
/// <see cref="UiNavigationCatalog"/>. The desktop sidebar renders <see cref="Primary"/> and
/// <see cref="Secondary"/>, or <see cref="Context"/> inside Admin or Settings; the mobile bottom
/// bar renders a bounded <see cref="MobilePrimary"/> set plus a More menu with every remaining
/// destination, so both layouts expose the same destinations. Labels are UI catalog keys.
/// </summary>
public sealed record UiShellNavigation(
    IReadOnlyList<UiNavigationItem> Primary,
    IReadOnlyList<UiNavigationItem> Secondary,
    IReadOnlyList<UiNavigationItem> MobilePrimary,
    IReadOnlyList<UiNavigationItem> MobileMore,
    UiNavigationContext? Context = null)
{
    public const int MaxMobilePrimaryItems = 4;

    public bool MoreIsActive => MobileMore.Any(x => x.IsActive);

    public static UiShellNavigation Build(
        PathString path,
        bool learningVisible,
        bool isOwner)
    {
        bool Visible(UiNavigationEntry entry) =>
            (!entry.OwnerOnly || isOwner) && (!entry.RequiresLearning || learningVisible);

        // Admin pages that live under /Settings belong to the Admin context, not to Settings.
        var inAdmin = isOwner && IsUnder(path, UiNavigationCatalog.Roots(UiNavigationCatalog.Admin).ToArray());
        var inSettings = !inAdmin && IsUnder(path, "/Settings", "/Appearance", "/LocalizationPreferences");

        var primary = UiNavigationCatalog.App
            .Where(Visible)
            .Select(entry => ToItem(entry, IsActive(entry, path)))
            .ToArray();
        var secondary = UiNavigationCatalog.Secondary
            .Where(Visible)
            .Select(entry => ToItem(entry, entry.Id switch
            {
                "admin" => inAdmin,
                "settings" => inSettings,
                _ => IsActive(entry, path)
            }))
            .ToArray();

        var all = primary.Concat(secondary).ToArray();
        var mobilePrimary = UiNavigationCatalog.MobilePrimarySlots
            .Select(slot => slot.Select(id => all.FirstOrDefault(item => item.Id == id)).FirstOrDefault(item => item is not null))
            .OfType<UiNavigationItem>()
            .Take(MaxMobilePrimaryItems)
            .ToArray();
        var mobileMore = all.Where(x => !mobilePrimary.Contains(x)).ToArray();

        var context = inAdmin
            ? BuildContext("admin", "nav.admin", UiNavigationCatalog.Admin, path, Visible)
            : inSettings
                ? BuildContext("settings", "nav.settings", UiNavigationCatalog.Settings, path, Visible)
                : null;

        return new UiShellNavigation(primary, secondary, mobilePrimary, mobileMore, context);
    }

    private static UiNavigationContext BuildContext(
        string id,
        string titleKey,
        UiNavigationSection[] sections,
        PathString path,
        Func<UiNavigationEntry, bool> visible)
    {
        // The most specific match wins, so "/Admin" (overview) is not active on "/Admin/Users".
        var entries = sections.SelectMany(section => section.Entries).Where(visible).ToArray();
        var active = entries
            .Select(entry => (entry, length: MatchLength(entry, path)))
            .Where(candidate => candidate.length >= 0)
            .OrderByDescending(candidate => candidate.length)
            .Select(candidate => candidate.entry.Id)
            .FirstOrDefault();

        var groups = sections
            .Select(section => new UiNavigationGroup(
                section.TitleKey,
                section.Entries.Where(visible).Select(entry => ToItem(entry, entry.Id == active)).ToArray()))
            .Where(group => group.Items.Count > 0)
            .ToArray();

        return new UiNavigationContext(id, titleKey, new UiNavigationItem("back", "nav.backToApp", "/", "back", false), groups);
    }

    private static UiNavigationItem ToItem(UiNavigationEntry entry, bool isActive) =>
        new(entry.Id, entry.LabelKey, entry.Href, entry.Icon, isActive);

    private static bool IsActive(UiNavigationEntry entry, PathString path) => MatchLength(entry, path) >= 0;

    /// <summary>Length of the matching root, or -1 when the entry does not match the path.</summary>
    private static int MatchLength(UiNavigationEntry entry, PathString path)
    {
        var href = UiNavigationCatalog.PathOf(entry.Href);
        if (entry.Exact)
        {
            var current = path.HasValue ? path.Value!.TrimEnd('/') : string.Empty;
            return string.Equals(current, href.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ? href.Length : -1;
        }

        return (entry.Matches ?? [href])
            .Where(root => path.StartsWithSegments(root, StringComparison.OrdinalIgnoreCase))
            .Select(root => root.Length)
            .DefaultIfEmpty(-1)
            .Max();
    }

    private static bool IsUnder(PathString path, params string[] roots) =>
        roots.Any(root => path.StartsWithSegments(root, StringComparison.OrdinalIgnoreCase));
}

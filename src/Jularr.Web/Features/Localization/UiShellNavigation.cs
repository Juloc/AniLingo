namespace Jularr.Web.Features.Localization;

/// <summary>
/// One rendered link. <see cref="Groups"/> is set on a section anchor (Admin, Settings) whose
/// child pages are listed beneath it, either expanded in the sidebar or on a drill-in screen.
/// </summary>
public sealed record UiNavigationItem(
    string Id,
    string LabelKey,
    string Href,
    string Icon,
    bool IsActive,
    IReadOnlyList<UiNavigationGroup>? Groups = null)
{
    public bool IsExpanded => Groups is not null;

    /// <summary>True when this link itself is the current page, not only its section.</summary>
    public bool IsCurrentPage =>
        IsActive && !(Groups?.SelectMany(group => group.Items).Any(item => item.IsActive) ?? false);
}

/// <summary>A titled group of child pages inside Admin or Settings.</summary>
public sealed record UiNavigationGroup(string TitleKey, IReadOnlyList<UiNavigationItem> Items);

/// <summary>
/// One destination in <see cref="UiNavigationCatalog"/>. <see cref="Matches"/> are the path roots
/// that mark it active (the href's path when empty); <see cref="Exact"/> matches the href only.
/// <see cref="Sections"/> are the grouped child pages of a section anchor.
/// </summary>
public sealed record UiNavigationEntry(
    string Id,
    string LabelKey,
    string Href,
    string Icon,
    string[]? Matches = null,
    bool Exact = false,
    bool OwnerOnly = false,
    bool RequiresLearning = false,
    UiNavigationSection[]? Sections = null);

public sealed record UiNavigationSection(string TitleKey, UiNavigationEntry[] Entries);

/// <summary>
/// Every shell destination in one place. To move, rename, regroup or hide a link, edit this
/// table only: sidebar, mobile bar, Profile, Library tabs and tests all read from it. Each page
/// appears once; lists that show a page again (Profile) refer to it by id.
/// </summary>
public static class UiNavigationCatalog
{
    /// <summary>Media types inside Library. The Library destination is active on all of them.</summary>
    public static readonly UiNavigationEntry[] LibraryTabs =
    [
        new("library-anime", "nav.libraryTab.anime", "/Library", "library"),
        new("library-reading", "nav.libraryTab.reading", "/Reading", "reading", ["/Reading", "/Novels", "/Manga"]),
        new("library-books", "nav.books", "/Books", "books")
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

    /// <summary>Personal pages only; server configuration belongs to <see cref="Admin"/>.</summary>
    public static readonly UiNavigationSection[] Settings =
    [
        new("nav.group.settingsYou",
        [
            new("settings-overview", "settings.nav.overview", "/Settings", "settings", Exact: true),
            new("settings-account", "settings.nav.account", "/Profile/Account", "profile"),
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

    public static readonly UiNavigationEntry[] App =
    [
        new("home", "nav.home", "/", "home", Exact: true),
        new("library", "nav.library", "/Library", "library", [.. Roots(LibraryTabs)]),
        new("watchlist", "nav.watchlist", "/Watchlist", "watchlist", ["/Watchlist", "/Franchises"]),
        new("calendar", "nav.calendar", "/Calendar", "calendar"),
        new("learn", "nav.learn", "/Learn", "learn", ["/Learn", "/Statistics", "/Kana"], RequiresLearning: true),
        new("activity", "nav.activity", "/Activity", "history")
    ];

    public static readonly UiNavigationEntry[] Secondary =
    [
        new("admin", "nav.admin", "/Admin", "admin", OwnerOnly: true, Sections: Admin),
        new("settings", "nav.settings", "/Settings", "settings", Sections: Settings),
        new("profile", "nav.profile", "/Profile", "profile")
    ];

    /// <summary>
    /// The Devices page belongs to #518. Set this once <c>Pages/Profile/Devices.cshtml</c>
    /// exists; a test keeps the two in sync.
    /// </summary>
    public static readonly bool DevicesPageAvailable = false;

    public static readonly UiNavigationEntry ProfileDevices =
        new("profile-devices", "nav.devices", "/Profile/Devices", "devices");

    /// <summary>The Profile page list, in order, by catalog id.</summary>
    public static readonly string[] ProfileLinkIds =
        ["settings-account", "activity", "settings-offline", "profile-devices", "settings", "admin"];

    /// <summary>Phone bottom bar, in order. Everything else is reached from Profile or search.</summary>
    public static readonly string[] MobilePrimaryIds = ["home", "calendar", "watchlist", "profile"];

    /// <summary>Readers whose sidebar shows the open book, novel or manga with its progress.</summary>
    public static readonly string[] CurrentReadingRoots = ["/Books/Read", "/Novels/Read", "/Manga/Read"];

    /// <summary>Every entry that has its own page, including section children.</summary>
    public static IEnumerable<UiNavigationEntry> All =>
        App.Concat(Secondary)
            .Concat(Admin.Concat(Settings).SelectMany(section => section.Entries))
            .Append(ProfileDevices);

    /// <summary>Every path root of a set of entries, used to decide which section a page belongs to.</summary>
    public static IEnumerable<string> Roots(IEnumerable<UiNavigationEntry> entries) =>
        entries.SelectMany(entry => entry.Matches ?? [PathOf(entry.Href)]);

    public static IEnumerable<string> Roots(UiNavigationSection[] sections) =>
        Roots(sections.SelectMany(section => section.Entries));

    public static string PathOf(string href)
    {
        var cut = href.IndexOfAny(['?', '#']);
        return cut < 0 ? href : href[..cut];
    }
}

/// <summary>
/// Canonical destination model for the shared app shell, built from
/// <see cref="UiNavigationCatalog"/>. The desktop sidebar renders <see cref="Primary"/> and
/// <see cref="Secondary"/>; inside Admin or Settings that item carries its grouped child pages
/// and the rest of the sidebar stays. The phone bottom bar renders <see cref="MobilePrimary"/>;
/// every other destination is on the Profile page (<see cref="BuildProfile"/>) or behind search.
/// Labels are UI catalog keys.
/// </summary>
public sealed record UiShellNavigation(
    IReadOnlyList<UiNavigationItem> Primary,
    IReadOnlyList<UiNavigationItem> Secondary,
    IReadOnlyList<UiNavigationItem> MobilePrimary,
    bool ShowCurrentReading = false)
{
    public const int MaxMobilePrimaryItems = 4;

    /// <summary>The section expanded in the sidebar, if any. Never more than one.</summary>
    public UiNavigationItem? Expanded => Secondary.FirstOrDefault(item => item.IsExpanded);

    public static UiShellNavigation Build(
        PathString path,
        bool learningVisible,
        bool isOwner)
    {
        // Admin pages that live under /Settings belong to Admin, not to Settings.
        var inAdmin = isOwner && IsUnder(path, UiNavigationCatalog.Roots(UiNavigationCatalog.Admin));
        var inSettings = !inAdmin && IsUnder(path, UiNavigationCatalog.Roots(UiNavigationCatalog.Settings));

        var primary = UiNavigationCatalog.App
            .Where(entry => Visible(entry, learningVisible, isOwner))
            .Select(entry => ToItem(entry, IsActive(entry, path)))
            .ToArray();
        var secondary = UiNavigationCatalog.Secondary
            .Where(entry => Visible(entry, learningVisible, isOwner))
            .Select(entry => entry.Id switch
            {
                "admin" => inAdmin ? Expand(entry, path, isOwner) : ToItem(entry, false),
                "settings" => inSettings ? Expand(entry, path, isOwner) : ToItem(entry, false),
                _ => ToItem(entry, !inAdmin && !inSettings && IsActive(entry, path))
            })
            .ToArray();

        // On a phone, every destination outside the bottom bar is reached through Profile.
        var all = primary.Concat(secondary).ToArray();
        var elsewhereActive = all.Any(item => item.IsActive && !UiNavigationCatalog.MobilePrimaryIds.Contains(item.Id));
        var mobilePrimary = UiNavigationCatalog.MobilePrimaryIds
            .Select(id => all.FirstOrDefault(item => item.Id == id))
            .OfType<UiNavigationItem>()
            .Select(item => item.Id == "profile" ? item with { IsActive = item.IsActive || elsewhereActive } : item)
            .Take(MaxMobilePrimaryItems)
            .ToArray();

        var showCurrentReading = !inAdmin && !inSettings && IsUnder(path, UiNavigationCatalog.CurrentReadingRoots);
        return new UiShellNavigation(primary, secondary, mobilePrimary, showCurrentReading);
    }

    /// <summary>
    /// The Profile page: <c>Links</c> in catalog order (Admin and Settings open their drill-in
    /// list), and <c>Elsewhere</c>, the remaining destinations the phone bottom bar has no room for.
    /// </summary>
    public static (IReadOnlyList<UiNavigationItem> Links, IReadOnlyList<UiNavigationItem> Elsewhere) BuildProfile(
        bool learningVisible,
        bool isOwner)
    {
        var entries = UiNavigationCatalog.All.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var links = UiNavigationCatalog.ProfileLinkIds
            .Where(id => id != UiNavigationCatalog.ProfileDevices.Id || UiNavigationCatalog.DevicesPageAvailable)
            .Select(id => entries[id])
            .Where(entry => Visible(entry, learningVisible, isOwner))
            .Select(entry => ToItem(entry, false) with
            {
                Href = entry.Sections is null ? entry.Href : DrillInHref(entry.Id)
            })
            .ToArray();

        var elsewhere = UiNavigationCatalog.App.Concat(UiNavigationCatalog.Secondary)
            .Where(entry => Visible(entry, learningVisible, isOwner))
            .Where(entry => !UiNavigationCatalog.MobilePrimaryIds.Contains(entry.Id)
                && !UiNavigationCatalog.ProfileLinkIds.Contains(entry.Id))
            .Select(entry => ToItem(entry, false))
            .ToArray();

        return (links, elsewhere);
    }

    /// <summary>The drill-in list of Admin or Settings, or null when the section is not available.</summary>
    public static UiNavigationItem? BuildSection(string? sectionId, bool isOwner)
    {
        var entry = UiNavigationCatalog.Secondary.FirstOrDefault(candidate =>
            candidate.Sections is not null
            && string.Equals(candidate.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        return entry is null || !Visible(entry, learningVisible: true, isOwner)
            ? null
            : Expand(entry, PathString.Empty, isOwner);
    }

    public static string DrillInHref(string sectionId) => $"/Profile/{sectionId}";

    /// <summary>Library media-type tabs; exactly one is active on any Library page.</summary>
    public static IReadOnlyList<UiNavigationItem> BuildLibraryTabs(PathString path) =>
        UiNavigationCatalog.LibraryTabs.Select(entry => ToItem(entry, IsActive(entry, path))).ToArray();

    private static bool Visible(UiNavigationEntry entry, bool learningVisible, bool isOwner) =>
        (!entry.OwnerOnly || isOwner) && (!entry.RequiresLearning || learningVisible);

    private static UiNavigationItem Expand(UiNavigationEntry anchor, PathString path, bool isOwner)
    {
        var entries = anchor.Sections!
            .SelectMany(section => section.Entries)
            .Where(entry => Visible(entry, learningVisible: true, isOwner))
            .ToArray();

        // The most specific match wins, so "/Admin" (overview) is not active on "/Admin/Users".
        var active = entries
            .Select(entry => (entry, length: MatchLength(entry, path)))
            .Where(candidate => candidate.length >= 0)
            .OrderByDescending(candidate => candidate.length)
            .Select(candidate => candidate.entry.Id)
            .FirstOrDefault();

        var groups = anchor.Sections!
            .Select(section => new UiNavigationGroup(
                section.TitleKey,
                section.Entries
                    .Where(entries.Contains)
                    .Select(entry => ToItem(entry, entry.Id == active))
                    .ToArray()))
            .Where(group => group.Items.Count > 0)
            .ToArray();

        return ToItem(anchor, path.HasValue) with { Groups = groups };
    }

    private static UiNavigationItem ToItem(UiNavigationEntry entry, bool isActive) =>
        new(entry.Id, entry.LabelKey, entry.Href, entry.Icon, isActive);

    private static bool IsActive(UiNavigationEntry entry, PathString path) => MatchLength(entry, path) >= 0;

    /// <summary>Length of the matching root, or -1 when the entry does not match the path.</summary>
    private static int MatchLength(UiNavigationEntry entry, PathString path)
    {
        if (!path.HasValue)
        {
            return -1;
        }

        var href = UiNavigationCatalog.PathOf(entry.Href);
        if (entry.Exact)
        {
            var current = path.Value!.TrimEnd('/');
            return string.Equals(current, href.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ? href.Length : -1;
        }

        return (entry.Matches ?? [href])
            .Where(root => path.StartsWithSegments(root, StringComparison.OrdinalIgnoreCase))
            .Select(root => root.Length)
            .DefaultIfEmpty(-1)
            .Max();
    }

    private static bool IsUnder(PathString path, IEnumerable<string> roots) =>
        roots.Any(root => path.StartsWithSegments(root, StringComparison.OrdinalIgnoreCase));
}

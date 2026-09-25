namespace AniLingo.Web.Features.Localization;

public static class UiTranslationResources
{
    public static IReadOnlyList<UiMessageDefinition> All { get; } =
    [
        M("nav.home", "Home", "Shell", "Navigation", "Primary navigation entry that opens the AniLingo home dashboard.", "short navigation label", 18),
        M("nav.discover", "Discover", "Shell", "Navigation", "Primary navigation entry for searching and browsing media.", "short navigation label", 18),
        M("nav.library", "Library", "Shell", "Navigation", "Primary navigation entry for the user's anime/media library.", "short navigation label", 18),
        M("nav.reading", "Reading", "Shell", "Navigation", "Primary navigation entry grouping novels and manga reading.", "short navigation label", 18),
        M("nav.learn", "Learning", "Shell", "Navigation", "Primary navigation entry for the optional learning hub.", "short navigation label", 18),
        M("nav.settings", "Settings", "Shell", "Navigation", "Primary navigation entry for personal application settings.", "short navigation label", 18),
        M("nav.admin", "Admin", "Shell", "Navigation", "Owner-only navigation entry for server administration.", "short navigation label", 18),

        M("common.save", "Save", "Common", "Button", "Commit the user's current edits or settings.", "concise action", 18),
        M("common.cancel", "Cancel", "Common", "Button", "Abandon the current edit/action without saving it.", "concise action", 18),
        M("common.search", "Search", "Common", "Button", "Start a search using the current query.", "concise action", 18),
        M("common.back", "Back", "Common", "Button", "Navigate back to the previous application surface.", "concise action", 18),

        M("home.continueWatching", "Continue watching", "Home", "Heading", "Heading for media episodes the user has started and can resume.", "friendly concise heading", 32),
        M("home.continueReading", "Continue reading", "Home", "Heading", "Heading for books, novels or manga the user has started and can resume.", "friendly concise heading", 32),

        M("home.eyebrow", "Japanese through your media", "Home", "Eyebrow", "Short label above the home dashboard hero. It describes optional Japanese/language tooling without making learning mandatory.", "compact product copy", 36),
        M("home.title", "Continue where you left off", "Home", "Heading", "Main home dashboard heading focused on resuming media and reading.", "friendly product heading", 42),
        M("home.subtitle", "Watch, read and pick up language tools when you want them.", "Home", "Body", "Home hero description emphasizing that language/learning tools are optional.", "friendly concise product copy", 80),
        M("home.reviewButton", "Review {count} due", "Home", "Button", "Optional learning shortcut showing the number of currently due reviews.", "concise learning action", 30,
            new Dictionary<string, string> { ["count"] = "Number of due review cards." }),
        M("home.metric.due", "Due reviews", "Home", "Metric label", "Label for the number of spaced-repetition reviews due now.", "compact metric label", 22),
        M("home.metric.ready", "Ready now", "Home", "Metric caption", "Caption indicating review items are ready now.", "compact status", 20),
        M("home.metric.anime", "Anime", "Home", "Metric label", "Label for anime count in the media library.", "compact metric label", 18),
        M("home.metric.inLibrary", "In your library", "Home", "Metric caption", "Caption for media count stored in the user's library.", "compact status", 24),
        M("home.metric.episodes", "Episodes", "Home", "Metric label", "Label for discovered episode count.", "compact metric label", 18),
        M("home.metric.discovered", "Discovered on NAS", "Home", "Metric caption", "Caption explaining that episode files were discovered on configured network/local storage.", "compact status", 28, null, ["NAS"]),
        M("home.library.eyebrow", "Library", "Home", "Eyebrow", "Short label above the recently discovered library section.", "compact section label", 18),
        M("home.library.recent", "Recently discovered", "Home", "Heading", "Heading for recently discovered media items.", "concise section heading", 32),
        M("home.library.view", "View library", "Home", "Link", "Link opening the full media library.", "concise navigation action", 24),
        M("home.empty.title", "No episodes yet", "Home", "Heading", "Empty-state heading when no anime episodes have been discovered.", "clear empty-state heading", 28),
        M("home.empty.body", "Add a media root in Settings and start a scan.", "Home", "Body", "Empty-state instruction for configuring a media library.", "clear instruction", 70),
        M("home.empty.settings", "Open settings", "Home", "Button", "Open Settings from the empty home library state.", "concise action", 24),

        M("learn.eyebrow", "Review", "Learning", "Eyebrow", "Short label above the spaced-repetition review screen.", "compact section label", 18),
        M("learn.title", "Learning", "Learning", "Heading", "Heading for the optional learning/review area.", "clear feature heading", 24),
        M("learn.dueNow", "{count} due now.", "Learning", "Status", "Shows how many review cards are due in the current session.", "compact status", 26,
            new Dictionary<string, string> { ["count"] = "Number of due review cards." }),
        M("learn.empty.title", "Nothing due", "Learning", "Heading", "Empty-state heading when there are no review cards due.", "friendly concise status", 24),
        M("learn.empty.body", "Saved learning items will appear here when they are due.", "Learning", "Body", "Explains the empty review queue without pressuring the user to study.", "neutral helpful copy", 72),
        M("learn.openLibrary", "Open library", "Learning", "Button", "Navigate from Learning to the media library.", "concise navigation action", 24),
        M("learn.showAnswer", "Show answer", "Learning", "Button", "Reveal the answer/back side of the current review card.", "concise review action", 24),
        M("learn.noMeaning", "No local dictionary meaning available.", "Learning", "Status", "Shown when the local dictionary has no meaning for the current item.", "neutral diagnostic", 54),
        M("learn.animeContext", "Anime context", "Learning", "Label", "Label for the source anime sentence attached to a review item.", "compact context label", 24),
        M("learn.explainAi", "Explain with AI", "Learning", "Button", "Request an optional AI explanation for the current source sentence.", "concise optional action", 24, null, ["AI"]),
        M("learn.rating.again", "Again", "Learning", "Button", "FSRS review rating meaning the user did not recall the answer.", "very short review rating", 14),
        M("learn.rating.hard", "Hard", "Learning", "Button", "FSRS review rating meaning recall was difficult.", "very short review rating", 14),
        M("learn.rating.good", "Good", "Learning", "Button", "FSRS review rating meaning normal successful recall.", "very short review rating", 14),
        M("learn.rating.easy", "Easy", "Learning", "Button", "FSRS review rating meaning effortless recall.", "very short review rating", 14),

        M("settings.language.title", "Interface language", "Localization", "Heading", "Personal setting for choosing the AniLingo UI language.", "clear settings heading", 32),
        M("settings.language.description", "Choose the language AniLingo uses for menus and interface text. This does not change content or learning languages.", "Localization", "Body", "Explains that UI language is independent from learning/content languages.", "clear settings copy", 120),
        M("settings.language.save", "Use this language", "Localization", "Button", "Save the selected UI locale for the current profile.", "concise settings action", 28),

        M("player.play", "Play", "Player", "Button", "Start or resume media playback.", "concise media action", 16),
        M("player.pause", "Pause", "Player", "Button", "Pause media playback at the current position.", "concise media action", 16),
        M("player.repeatLine", "Repeat line", "Player", "Button", "Replay the subtitle or dialogue cue currently visible in the media player.", "concise media action", 24),
        M("player.learnThisLine", "Learn this line", "Player", "Button", "Open optional language or learning tools for the currently visible subtitle sentence.", "concise learning action", 28),

        M("learning.saveWord", "Save word", "Learning", "Button", "Save the selected word to the user's vocabulary without necessarily starting SRS reviews.", "concise action", 24),
        M("learning.markKnown", "I know this", "Learning", "Button", "Mark the selected vocabulary item as already known by the user.", "natural first-person action", 24),
        M("learning.ignoreWord", "Ignore", "Learning", "Button", "Exclude the selected vocabulary item from learning suggestions.", "concise action", 18),
        M("learning.reviewDue", "{count} reviews due", "Learning", "Status", "Shows how many spaced-repetition reviews are currently due.", "compact status", 32,
            new Dictionary<string, string> { ["count"] = "Number of currently due review cards." }),
        M("learning.languageTools", "Language tools", "Learning", "Heading", "Label for optional lookup, reading, translation and explanation tools that do not require active study.", "clear feature label", 28),

        M("reader.continueReading", "Continue reading", "Reading", "Button", "Resume the current book, novel or manga at the saved reading position.", "concise action", 28),

        M("admin.languages.title", "UI languages", "Localization", "Heading", "Owner administration page for application-interface languages and translations.", "administrative heading", 32),
        M("admin.languages.add", "Add language", "Localization", "Button", "Add a new BCP-47 application locale to the translation catalog.", "concise admin action", 24),
        M("admin.languages.generateMissing", "Generate missing", "Localization", "Button", "Use the configured AI provider to create all currently missing translations for the selected UI locale.", "concise admin action", 28),
        M("admin.languages.regenerateOutdated", "Regenerate outdated", "Localization", "Button", "Use AI to regenerate translations whose source text or translation context changed.", "concise admin action", 32),
        M("admin.languages.markReviewed", "Mark reviewed", "Localization", "Button", "Confirm that a generated translation has been checked by a human.", "concise admin action", 24)
    ];

    private static readonly IReadOnlyDictionary<string, UiMessageDefinition> ByKeyMap =
        All.ToDictionary(x => x.Key, StringComparer.Ordinal);

    public static bool TryGet(string key, out UiMessageDefinition message) =>
        ByKeyMap.TryGetValue(key, out message!);

    public static UiMessageDefinition Get(string key) =>
        TryGet(key, out var message)
            ? message
            : throw new KeyNotFoundException($"Unknown UI translation key '{key}'.");

    private static UiMessageDefinition M(
        string key,
        string defaultText,
        string feature,
        string surface,
        string description,
        string tone,
        int? maxLength = null,
        IReadOnlyDictionary<string, string>? placeholders = null,
        IReadOnlyList<string>? doNotTranslate = null) =>
        new(
            key,
            defaultText,
            feature,
            surface,
            description,
            tone,
            maxLength,
            placeholders,
            doNotTranslate);
}

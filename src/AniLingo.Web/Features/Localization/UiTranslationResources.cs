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

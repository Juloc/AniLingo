using System.Globalization;
using AniLingo.Web.Features.Learning.Courses;

namespace AniLingo.Web.Features.Learning.LanguageAssistance;

/// <summary>Content kinds that can host the shared language inspector.</summary>
public enum LanguageSourceType
{
    Anime = 1,
    Novel = 2,
    Book = 3,
    Manga = 4
}

/// <summary>
/// Language assistance a scope allows, resolved once through
/// <see cref="LearningModuleResolver.ResolveAssistanceAsync"/>. Lookup,
/// readings and explanations never create cards; only <see cref="CanSave"/>
/// (Vocabulary) and <see cref="CanLearn"/> (Vocabulary and Reviews) change
/// Learning card state.
/// </summary>
public sealed record LanguageAssistanceAvailability(
    bool Lookup,
    bool Readings,
    bool Explanations,
    bool Vocabulary,
    bool Reviews,
    bool SurfaceTools)
{
    public static LanguageAssistanceAvailability None { get; } =
        new(false, false, false, false, false, false);

    /// <summary>The inspector has something to show for this scope.</summary>
    public bool Any => Lookup || Readings || Explanations;

    /// <summary>The player/reader integration renders the inspector at all.</summary>
    public bool ShowInspector => Any && SurfaceTools;

    /// <summary>Save, Known and Ignore: canonical Learning card transitions.</summary>
    public bool CanSave => Vocabulary;

    /// <summary>Start spaced repetition for a word.</summary>
    public bool CanLearn => Vocabulary && Reviews;

    public static LanguageAssistanceAvailability From(
        LearningResolvedSettings settings,
        LanguageSourceType? surface) =>
        new(
            settings.IsEnabled(LearningCapability.LanguageLookup),
            settings.IsEnabled(LearningCapability.ReadingAids),
            settings.IsEnabled(LearningCapability.AiExplanations),
            settings.IsEnabled(LearningCapability.Vocabulary),
            settings.IsEnabled(LearningCapability.Reviews),
            surface switch
            {
                null => true,
                LanguageSourceType.Anime => settings.IsEnabled(LearningCapability.PlayerTools),
                _ => settings.IsEnabled(LearningCapability.ReaderTools)
            });
}

/// <summary>
/// Typed position inside a source: an anime cue start, a novel/book paragraph or
/// a manga page with an optional region.
/// </summary>
public sealed record LanguageSourcePosition(
    int? CueStartMs = null,
    int? Paragraph = null,
    int? Page = null,
    int? Region = null);

/// <summary>
/// Source-agnostic anchor of inspected text. It maps onto the Learning
/// hierarchy (<see cref="Scope"/>) and onto a persisted
/// <see cref="LearningContext"/> (<see cref="SourceType"/>,
/// <see cref="SourceKey"/>, <see cref="PositionKey"/>):
/// <list type="bullet">
/// <item>anime: <c>episode:{episodeId}</c> + <c>cue:{startMs}</c></item>
/// <item>novel/book: <c>chapter:{chapterId}</c> + <c>paragraph:{index}</c></item>
/// <item>manga: <c>chapter:{chapterId}</c> + <c>page:{page}</c> or <c>page:{page}#region:{region}</c></item>
/// </list>
/// </summary>
public sealed record LanguageSourceAnchor(
    LanguageSourceType Type,
    string WorkKey,
    Guid ContentId,
    LanguageSourcePosition Position)
{
    public string SourceType => SourceTypeKey(Type);

    public string SourceKey =>
        Type == LanguageSourceType.Anime
            ? $"episode:{ContentId:D}"
            : $"chapter:{ContentId:D}";

    public string? PositionKey =>
        Type switch
        {
            LanguageSourceType.Anime when Position.CueStartMs is >= 0 =>
                $"cue:{Position.CueStartMs.Value.ToString(CultureInfo.InvariantCulture)}",
            LanguageSourceType.Novel or LanguageSourceType.Book when Position.Paragraph is >= 0 =>
                $"paragraph:{Position.Paragraph.Value.ToString(CultureInfo.InvariantCulture)}",
            LanguageSourceType.Manga when Position.Page is >= 1 && Position.Region is >= 0 =>
                $"page:{Position.Page.Value.ToString(CultureInfo.InvariantCulture)}#region:{Position.Region.Value.ToString(CultureInfo.InvariantCulture)}",
            LanguageSourceType.Manga when Position.Page is >= 1 =>
                $"page:{Position.Page.Value.ToString(CultureInfo.InvariantCulture)}",
            _ => null
        };

    public LearningMediaType MediaType => ToMediaType(Type);

    public LearningScopeContext Scope =>
        new(MediaType, WorkKey, ContentId.ToString("D"));

    public LearningContextInput ToContext(string languageTag, string text) =>
        new(SourceType, SourceKey, PositionKey, languageTag, text);

    public static string SourceTypeKey(LanguageSourceType type) =>
        type.ToString().ToLowerInvariant();

    public static LearningMediaType ToMediaType(LanguageSourceType type) =>
        type switch
        {
            LanguageSourceType.Anime => LearningMediaType.Anime,
            LanguageSourceType.Novel => LearningMediaType.Novel,
            LanguageSourceType.Book => LearningMediaType.Book,
            LanguageSourceType.Manga => LearningMediaType.Manga,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

    public static bool TryParseType(string? value, out LanguageSourceType type)
    {
        type = default;
        return !string.IsNullOrWhiteSpace(value)
            && !int.TryParse(value, out _)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out type)
            && Enum.IsDefined(type);
    }

    /// <summary>
    /// Reads a persisted <see cref="LearningContext"/> anchor back into its
    /// source type, content and typed position.
    /// </summary>
    public static bool TryParseStored(
        string sourceType,
        string sourceKey,
        string? positionKey,
        out LanguageSourceType type,
        out Guid contentId,
        out LanguageSourcePosition position)
    {
        contentId = Guid.Empty;
        position = new LanguageSourcePosition();
        if (!TryParseType(sourceType, out type))
        {
            return false;
        }

        var prefix = type == LanguageSourceType.Anime ? "episode:" : "chapter:";
        if (!sourceKey.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParse(sourceKey[prefix.Length..], out contentId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(positionKey))
        {
            return true;
        }

        position = type switch
        {
            LanguageSourceType.Anime => new LanguageSourcePosition(
                CueStartMs: ReadNumber(positionKey, "cue:")),
            LanguageSourceType.Novel or LanguageSourceType.Book => new LanguageSourcePosition(
                Paragraph: ReadNumber(positionKey, "paragraph:")),
            _ => ReadMangaPosition(positionKey)
        };

        return true;
    }

    private static LanguageSourcePosition ReadMangaPosition(string positionKey)
    {
        var parts = positionKey.Split('#', 2);
        return new LanguageSourcePosition(
            Page: ReadNumber(parts[0], "page:"),
            Region: parts.Length == 2 ? ReadNumber(parts[1], "region:") : null);
    }

    private static int? ReadNumber(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                value[prefix.Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number)
            ? number
            : null;
}

/// <summary>
/// Where inspected text comes from. Omit <see cref="SourceType"/> on Learning
/// Hub pages (profile scope, no context anchor). The work is always derived on
/// the server from the content, so a client cannot pick a foreign scope.
/// </summary>
public sealed record LanguageInspectContext(
    string? Language,
    string? SourceType = null,
    string? ContentKey = null,
    string? Sentence = null,
    int? CueStartMs = null,
    int? Paragraph = null,
    int? Page = null,
    int? Region = null);

public sealed record LanguageInspectRequest(
    string? Text,
    LanguageInspectContext? Context);

public sealed record LanguageWordStateRequest(
    string? Text,
    string? State,
    LanguageInspectContext? Context);

public sealed record LanguageExplainRequest(
    string? Sentence,
    LanguageInspectContext? Context);

/// <summary>What the client may offer for the inspected text.</summary>
public sealed record LanguageInspectorFeatures(
    bool Lookup,
    bool Readings,
    bool Explanation,
    bool Save,
    bool Learn)
{
    public static LanguageInspectorFeatures From(
        LanguageAssistanceAvailability availability,
        bool explanationSupported) =>
        new(
            availability.Lookup,
            availability.Readings,
            availability.Explanations && explanationSupported,
            availability.CanSave,
            availability.CanLearn);
}

/// <summary>
/// One piece of inspected text. Plain tokens (punctuation, spaces) are not
/// interactive. Reading, meaning and state are only present when the scope
/// allows readings, lookup or vocabulary respectively; <see cref="State"/> is
/// null for words the profile does not track.
/// </summary>
public sealed record LanguageInspectorToken(
    string Surface,
    string? Canonical,
    string? Reading,
    string? Meaning,
    string? MeaningLanguage,
    bool Interactive,
    string? State);

public sealed record LanguageInspection(
    string Language,
    string Text,
    string? Sentence,
    LanguageInspectorFeatures Features,
    IReadOnlyList<LanguageInspectorToken> Tokens,
    Ai.AiSentenceExplanation? Explanation);

public sealed record LanguageWordState(
    string Text,
    string Language,
    string State,
    bool ContextRecorded);

/// <summary>Word states the inspector can set. Suspend stays on the Vocabulary page.</summary>
public static class LanguageWordStates
{
    public const string Saved = "saved";
    public const string Learning = "learning";
    public const string Known = "known";
    public const string Ignored = "ignored";

    public static string Key(UserTermState state) =>
        state.ToString().ToLowerInvariant();

    public static bool TryParse(string? value, out UserTermState state)
    {
        state = value?.Trim().ToLowerInvariant() switch
        {
            Saved => UserTermState.Saved,
            Learning => UserTermState.Learning,
            Known => UserTermState.Known,
            Ignored => UserTermState.Ignored,
            _ => 0
        };

        return state != 0;
    }
}

/// <summary>The inspector could not run because the scope does not allow it.</summary>
public sealed class LanguageAssistanceDeniedException(string message) : Exception(message);

/// <summary>
/// Page host of the shared <c>_LanguageInspector</c> partial. The factory
/// methods describe each integration: which source the page shows, the content
/// language and, for readers, which element holds selectable text.
/// </summary>
public sealed record LanguageInspectorHost(
    LanguageSourceType? SourceType,
    Guid? WorkId,
    Guid? ContentId,
    string? Language,
    string? SelectionSurface = null,
    string? ParagraphAttribute = null)
{
    /// <summary>Learning Hub pages: profile scope, context per inspected element.</summary>
    public static LanguageInspectorHost LearningHub() =>
        new(null, null, null, null);

    public static LanguageInspectorHost ForAnimeEpisode(
        Guid animeId,
        Guid episodeId,
        string language) =>
        new(LanguageSourceType.Anime, animeId, episodeId, language);

    public static LanguageInspectorHost ForBookChapter(
        Guid workId,
        Guid chapterId,
        string language) =>
        new(
            LanguageSourceType.Book,
            workId,
            chapterId,
            language,
            "[data-book-original]",
            "data-book-paragraph");

    public static LanguageInspectorHost ForNovelChapter(
        Guid workId,
        Guid chapterId,
        string language,
        string? selectionSurface = null,
        string? paragraphAttribute = null) =>
        new(
            LanguageSourceType.Novel,
            workId,
            chapterId,
            language,
            selectionSurface,
            paragraphAttribute);

    public static LanguageInspectorHost ForMangaChapter(
        Guid seriesId,
        Guid chapterId,
        string language) =>
        new(LanguageSourceType.Manga, seriesId, chapterId, language);

    public LearningScopeContext? Scope =>
        SourceType is { } type && WorkId is { } work && ContentId is { } content
            ? new LearningScopeContext(
                LanguageSourceAnchor.ToMediaType(type),
                work.ToString("D"),
                content.ToString("D"))
            : null;
}

/// <summary>
/// The AI sentence explainer (Features/Ai) is prompted only for languages whose
/// toolkit supports readings (Japanese today); other languages get lookup and
/// vocabulary actions without it. Gated by toolkit capability rather than a
/// language tag check so the explainer follows whichever toolkit actually
/// backs the language instead of a duplicated "is this Japanese" test.
/// </summary>
public static class SentenceExplanationSupport
{
    public static bool Supports(string languageTag) =>
        LearningLanguageTag.TryNormalize(languageTag, out var normalized)
        && LearningLanguageToolkitRegistry.Supports(normalized, LearningLanguageCapability.Readings);
}

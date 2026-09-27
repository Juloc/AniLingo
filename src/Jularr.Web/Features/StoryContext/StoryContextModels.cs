namespace Jularr.Web.Features.StoryContext;

/// <summary>
/// Shared, language-neutral story memory of one work (book, light novel or
/// web novel). Every AI feature reads the same extracted knowledge through
/// <see cref="StoryContextBuilder"/>; consumer-specific data (for example the
/// target-language names of the translation bible) lives in the consumer and
/// references records here by name.
/// </summary>
public sealed class StoryContextDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public Guid WorkId { get; set; }
    public string SourceLanguage { get; set; } = "und";

    /// <summary>
    /// Bumped on every persisted change. Snapshot caches key on it; snapshot
    /// hashes are computed from the filtered content, so a change that does
    /// not affect a chapter boundary keeps that boundary's hash stable.
    /// </summary>
    public long Revision { get; set; }

    public StoryStyle Style { get; set; } = new();
    public List<string> Themes { get; set; } = [];

    /// <summary>
    /// Highest chapter whose text informed the book-level analysis (style and
    /// themes). Null when unknown; spoiler-safe snapshots then leave the
    /// book-level analysis out.
    /// </summary>
    public int? AnalysisThroughChapter { get; set; }

    public List<StoryEntity> Entities { get; set; } = [];
    public List<StoryTerm> Terms { get; set; } = [];
    public List<StoryChapterMemory> Chapters { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StoryStyle
{
    public string? NarrativePerspective { get; set; }
    public string? OverallStyle { get; set; }
    public string? Register { get; set; }
    public string? Audience { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(NarrativePerspective)
        && string.IsNullOrWhiteSpace(OverallStyle)
        && string.IsNullOrWhiteSpace(Register)
        && string.IsNullOrWhiteSpace(Audience);
}

/// <summary>
/// A character, location, item, organization or concept. The current fields
/// hold the latest known facts; <see cref="History"/> keeps compact
/// per-chapter states so a snapshot for an earlier boundary never sees facts
/// learned later.
/// </summary>
public sealed class StoryEntity
{
    public string Name { get; set; } = "";

    /// <summary>Free-form type as extracted (character, place, magic item…).</summary>
    public string Type { get; set; } = "entity";

    public List<string> Aliases { get; set; } = [];
    public string? Description { get; set; }
    public string? Pronouns { get; set; }
    public string? Relationships { get; set; }
    public string? VoiceNotes { get; set; }
    public string? Appearance { get; set; }

    /// <summary>
    /// Earliest chapter known to mention the entity. Null means unknown
    /// (analysis seed, manual edit or legacy data not yet resolved); such
    /// entities never enter a spoiler-safe snapshot.
    /// </summary>
    public int? FirstSeenChapter { get; set; }

    public int? LastChangedChapter { get; set; }
    public string Origin { get; set; } = StoryFactOrigins.Chapter;

    /// <summary>Chapters 1..N were searched for the name without a match.</summary>
    public int TextScanThrough { get; set; }

    public List<StoryEntityState> History { get; set; } = [];

    public StoryEntityKind Kind => StoryEntityKinds.Classify(Type);
}

/// <summary>Facts about an entity as known at the end of <see cref="Chapter"/>.</summary>
public sealed class StoryEntityState
{
    public int Chapter { get; set; }
    public string? Description { get; set; }
    public string? Pronouns { get; set; }
    public string? Relationships { get; set; }
    public string? Appearance { get; set; }
}

public sealed class StoryTerm
{
    public string Source { get; set; } = "";
    public string Category { get; set; } = "term";
    public int? FirstSeenChapter { get; set; }
    public string Origin { get; set; } = StoryFactOrigins.Chapter;
    public int TextScanThrough { get; set; }
}

public sealed class StoryChapterMemory
{
    public Guid ChapterId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? ContinuityNotes { get; set; }

    /// <summary>Source hash of the chapter text the memory was extracted from.</summary>
    public string? SourceHash { get; set; }

    /// <summary>Which consumer produced the extraction (translation:de, story…).</summary>
    public string? ExtractedBy { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class StoryFactOrigins
{
    public const string Analysis = "analysis";
    public const string Chapter = "chapter";
    public const string Manual = "manual";
    public const string Legacy = "legacy";
    public const string TextScan = "text-scan";
}

public enum StoryEntityKind
{
    Character,
    Location,
    Item,
    Organization,
    Concept,
    Other
}

public static class StoryEntityKinds
{
    public static StoryEntityKind Classify(string? type)
    {
        var value = type?.Trim().ToLowerInvariant() ?? "";

        if (ContainsAny(value, "character", "person", "people", "creature", "animal", "god", "deity", "spirit"))
        {
            return StoryEntityKind.Character;
        }

        if (ContainsAny(value, "place", "location", "city", "town", "village", "country", "kingdom", "realm", "region", "building", "room", "world", "planet", "land", "school", "academy"))
        {
            return StoryEntityKind.Location;
        }

        if (ContainsAny(value, "organization", "organisation", "faction", "group", "guild", "family", "clan", "order"))
        {
            return StoryEntityKind.Organization;
        }

        if (ContainsAny(value, "item", "object", "weapon", "artifact", "artefact", "tool", "vehicle", "ship"))
        {
            return StoryEntityKind.Item;
        }

        if (ContainsAny(value, "concept", "magic", "spell", "skill", "ability", "event", "title", "rank"))
        {
            return StoryEntityKind.Concept;
        }

        return StoryEntityKind.Other;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.Ordinal));
}

/// <summary>Neutral entity facts supplied by an extraction, seed or edit.</summary>
public sealed record StoryEntityInput(
    string Name,
    string? Type,
    string? Description,
    string? Pronouns,
    string? Relationships,
    string? VoiceNotes,
    string? Appearance = null,
    IReadOnlyList<string>? Aliases = null);

public sealed record StoryTermInput(
    string Source,
    string? Category);

public sealed record StorySeedInput(
    string? NarrativePerspective,
    string? OverallStyle,
    string? Register,
    string? Audience,
    IReadOnlyList<string> Themes,
    IReadOnlyList<StoryEntityInput> Entities,
    IReadOnlyList<StoryTermInput> Terms,
    int? AnalysisThroughChapter);

public sealed record StoryChapterInput(
    Guid ChapterId,
    int Number,
    string Title,
    string? Summary,
    string? ContinuityNotes,
    string? SourceHash,
    string ExtractedBy,
    IReadOnlyList<StoryEntityInput> Entities,
    IReadOnlyList<StoryTermInput> Terms);

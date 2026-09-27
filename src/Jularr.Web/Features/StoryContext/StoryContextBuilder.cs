using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jularr.Web.Features.StoryContext;

public enum StoryContextScope
{
    /// <summary>Everything known, regardless of chapter (translation, owner tools).</summary>
    Full,

    /// <summary>
    /// Spoiler-safe: only what a reader knows before opening the chapter.
    /// Nothing first introduced in the chapter or later is included.
    /// </summary>
    BeforeChapter,

    /// <summary>What a reader knows after finishing the chapter.</summary>
    AfterChapter
}

[Flags]
public enum StoryContextFields
{
    None = 0,
    Style = 1,
    Themes = 2,
    Entities = 4,
    EntityDetails = 8,
    Appearance = 16,
    Terms = 32,
    RecentChapters = 64,
    EarlierChapters = 128,
    All = Style | Themes | Entities | EntityDetails | Appearance | Terms | RecentChapters | EarlierChapters
}

/// <summary>
/// Which projection of the shared story memory a consumer needs. Consumers
/// ask only for the fields and sizes they use; the builder owns filtering,
/// ordering, compaction and chapter boundaries.
/// </summary>
public sealed record StoryContextQuery
{
    public StoryContextScope Scope { get; init; } = StoryContextScope.Full;

    /// <summary>
    /// Boundary chapter for <see cref="StoryContextScope.BeforeChapter"/> and
    /// <see cref="StoryContextScope.AfterChapter"/>. In
    /// <see cref="StoryContextScope.Full"/> it only positions the recent
    /// chapter window (chapters before it).
    /// </summary>
    public int? Chapter { get; init; }

    public StoryContextFields Fields { get; init; } = StoryContextFields.All;

    /// <summary>When set, only entities and terms mentioned in this text (or pinned) are included.</summary>
    public string? RelevantText { get; init; }

    public IReadOnlyCollection<string>? PinnedTerms { get; init; }
    public Func<StoryEntity, bool>? EntityFilter { get; init; }
    public Func<StoryTerm, bool>? TermFilter { get; init; }

    public int MaxEntities { get; init; } = 24;
    public int MaxTerms { get; init; } = 48;
    public int MaxThemes { get; init; } = 12;
    public int RecentChapterCount { get; init; } = 3;
    public int MaxEarlierChapters { get; init; } = 12;

    /// <summary>True when the query can be cached by its value (no delegates, no free text).</summary>
    public bool IsCacheable =>
        EntityFilter is null
        && TermFilter is null
        && RelevantText is null
        && PinnedTerms is null;

    public string CacheKey =>
        $"{Scope}|{Chapter}|{(int)Fields}|{MaxEntities}|{MaxTerms}|{MaxThemes}|{RecentChapterCount}|{MaxEarlierChapters}";

    public static StoryContextQuery Before(int chapter) =>
        new()
        {
            Scope = StoryContextScope.BeforeChapter,
            Chapter = chapter
        };

    public static StoryContextQuery After(int chapter) =>
        new()
        {
            Scope = StoryContextScope.AfterChapter,
            Chapter = chapter
        };
}

public sealed record StoryStyleView(
    string? NarrativePerspective,
    string? OverallStyle,
    string? Register,
    string? Audience);

public sealed record StoryEntityView(
    string Name,
    string Type,
    StoryEntityKind Kind,
    IReadOnlyList<string> Aliases,
    string? Description,
    string? Pronouns,
    string? Relationships,
    string? VoiceNotes,
    string? Appearance,
    int? FirstSeenChapter);

public sealed record StoryTermView(
    string Source,
    string Category,
    int? FirstSeenChapter);

public sealed record StoryChapterView(
    int Number,
    string Title,
    string? Summary,
    string? ContinuityNotes);

public sealed record StoryContextSnapshot(
    Guid WorkId,
    StoryContextScope Scope,
    int? Chapter,
    long Revision,
    StoryStyleView? Style,
    IReadOnlyList<string> Themes,
    IReadOnlyList<StoryEntityView> Entities,
    IReadOnlyList<StoryTermView> Terms,
    IReadOnlyList<StoryChapterView> RecentChapters,
    IReadOnlyList<StoryChapterView> EarlierChapters,
    string Hash)
{
    /// <summary>False for chapter 1 or a work without extracted knowledge before the boundary.</summary>
    public bool HasStoryKnowledge =>
        Entities.Count > 0
        || Terms.Count > 0
        || RecentChapters.Count > 0
        || EarlierChapters.Count > 0;

    public static StoryContextSnapshot Empty(
        Guid workId,
        StoryContextQuery query) =>
        StoryContextBuilder.Build(
            new StoryContextDocument { WorkId = workId },
            query);
}

/// <summary>Explicit character budgets for generated context (≈4 characters per token).</summary>
public static class StoryContextBudgets
{
    public const int TranslationChunk = 4200;
    public const int TranslationMemory = 5000;
    public const int TranslationFull = 12000;
    public const int Extraction = 3000;
    public const int ImagePrompt = 2400;
    public const int EarlierChapterLine = 180;
}

/// <summary>
/// The central context builder: turns the shared story memory into a
/// bounded, deterministic snapshot for one consumer and chapter boundary.
/// </summary>
public static class StoryContextBuilder
{
    private static readonly JsonSerializerOptions HashOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static StoryContextSnapshot Build(
        StoryContextDocument document,
        StoryContextQuery query)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Scope != StoryContextScope.Full)
        {
            if (query.Chapter is not int boundary || boundary < 1)
            {
                throw new ArgumentException(
                    "Chapter-bounded story context needs a chapter number of at least 1.",
                    nameof(query));
            }
        }

        // Highest chapter whose knowledge may be used.
        var knownThrough = query.Scope switch
        {
            StoryContextScope.BeforeChapter => query.Chapter!.Value - 1,
            StoryContextScope.AfterChapter => query.Chapter!.Value,
            _ => int.MaxValue
        };
        var bounded = query.Scope != StoryContextScope.Full;
        var fields = query.Fields;

        var analysisVisible = !bounded
            || document.AnalysisThroughChapter is int through && through <= knownThrough;

        StoryStyleView? style = null;
        if (fields.HasFlag(StoryContextFields.Style)
            && analysisVisible
            && !document.Style.IsEmpty)
        {
            style = new StoryStyleView(
                document.Style.NarrativePerspective,
                document.Style.OverallStyle,
                document.Style.Register,
                document.Style.Audience);
        }

        var themes = fields.HasFlag(StoryContextFields.Themes) && analysisVisible
            ? document.Themes.Take(Math.Max(0, query.MaxThemes)).ToArray()
            : [];

        var entities = fields.HasFlag(StoryContextFields.Entities)
            ? SelectEntities(document, query, bounded, knownThrough)
            : [];

        var terms = fields.HasFlag(StoryContextFields.Terms)
            ? SelectTerms(document, query, bounded, knownThrough)
            : [];

        var windowEnd = !bounded && query.Chapter is int positioned
            ? positioned - 1
            : knownThrough;

        var visibleChapters = document.Chapters
            .Where(x => x.Number <= windowEnd)
            .OrderBy(x => x.Number)
            .ToArray();

        var recentCount = fields.HasFlag(StoryContextFields.RecentChapters)
            ? Math.Max(0, query.RecentChapterCount)
            : 0;
        var recent = visibleChapters
            .TakeLast(recentCount)
            .Select(x => new StoryChapterView(x.Number, x.Title, x.Summary, x.ContinuityNotes))
            .ToArray();

        var earlier = fields.HasFlag(StoryContextFields.EarlierChapters)
            ? visibleChapters
                .SkipLast(recent.Length)
                .TakeLast(Math.Max(0, query.MaxEarlierChapters))
                .Select(x => new StoryChapterView(
                    x.Number,
                    x.Title,
                    CompactSummary(x.Summary, StoryContextBudgets.EarlierChapterLine),
                    null))
                .ToArray()
            : [];

        var hash = ComputeHash(query, style, themes, entities, terms, recent, earlier);

        return new StoryContextSnapshot(
            document.WorkId,
            query.Scope,
            query.Chapter,
            document.Revision,
            style,
            themes,
            entities,
            terms,
            recent,
            earlier,
            hash);
    }

    /// <summary>True when any of <paramref name="names"/> occurs in <paramref name="text"/>.</summary>
    public static bool IsMentioned(string text, IEnumerable<string> names) =>
        names.Any(name =>
            !string.IsNullOrWhiteSpace(name)
            && text.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Appends lines in priority order until the budget is reached.</summary>
    public static string FitLines(IEnumerable<string> lines, int maxCharacters)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (builder.Length + line.Length + Environment.NewLine.Length > maxCharacters)
            {
                if (builder.Length == 0)
                {
                    return line.Length <= maxCharacters ? line : line[..maxCharacters];
                }

                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Hard cap used where the legacy prompt format requires plain truncation.</summary>
    public static string Truncate(string value, int maxCharacters) =>
        value.Length <= maxCharacters
            ? value
            : value[..maxCharacters];

    public static string? CompactSummary(string? summary, int maxCharacters)
    {
        var clean = summary?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        var sentenceEnd = clean.IndexOfAny(['.', '!', '?', '。', '！', '？']);
        if (sentenceEnd > 0 && sentenceEnd + 1 <= maxCharacters)
        {
            return clean[..(sentenceEnd + 1)];
        }

        return clean.Length <= maxCharacters
            ? clean
            : clean[..(maxCharacters - 1)].TrimEnd() + "…";
    }

    private static StoryEntityView[] SelectEntities(
        StoryContextDocument document,
        StoryContextQuery query,
        bool bounded,
        int knownThrough)
    {
        var withDetails = query.Fields.HasFlag(StoryContextFields.EntityDetails);
        var withAppearance = query.Fields.HasFlag(StoryContextFields.Appearance);

        var candidates = document.Entities
            .Select((entity, index) => (entity, index))
            .Where(x => !bounded
                || x.entity.FirstSeenChapter is int seen && seen <= knownThrough)
            .Where(x => query.EntityFilter?.Invoke(x.entity) != false);

        if (query.RelevantText is string text)
        {
            candidates = candidates.Where(x =>
                IsMentioned(text, x.entity.Aliases.Prepend(x.entity.Name)));
        }
        else if (bounded)
        {
            // No text to match: prefer what the reader met most recently.
            candidates = candidates
                .OrderByDescending(x => LatestState(x.entity, knownThrough)?.Chapter ?? x.entity.FirstSeenChapter ?? 0)
                .ThenBy(x => x.entity.Kind == StoryEntityKind.Character ? 0 : 1)
                .ThenBy(x => x.index);
        }

        return candidates
            .Take(Math.Max(0, query.MaxEntities))
            .Select(x => Project(x.entity, bounded, knownThrough, withDetails, withAppearance))
            .ToArray();
    }

    private static StoryEntityView Project(
        StoryEntity entity,
        bool bounded,
        int knownThrough,
        bool withDetails,
        bool withAppearance)
    {
        if (!bounded)
        {
            return new StoryEntityView(
                entity.Name,
                entity.Type,
                entity.Kind,
                entity.Aliases.ToArray(),
                withDetails ? entity.Description : null,
                withDetails ? entity.Pronouns : null,
                withDetails ? entity.Relationships : null,
                withDetails ? entity.VoiceNotes : null,
                withAppearance ? entity.Appearance : null,
                entity.FirstSeenChapter);
        }

        // Bounded: only facts recorded for chapters inside the boundary.
        // Aliases carry no chapter and can reveal identities, so they stay out.
        var state = LatestState(entity, knownThrough);
        return new StoryEntityView(
            entity.Name,
            entity.Type,
            entity.Kind,
            [],
            withDetails ? state?.Description : null,
            withDetails ? state?.Pronouns : null,
            withDetails ? state?.Relationships : null,
            VoiceNotes: null,
            withAppearance ? state?.Appearance : null,
            entity.FirstSeenChapter);
    }

    private static StoryEntityState? LatestState(StoryEntity entity, int knownThrough) =>
        entity.History
            .Where(x => x.Chapter <= knownThrough)
            .MaxBy(x => x.Chapter);

    private static StoryTermView[] SelectTerms(
        StoryContextDocument document,
        StoryContextQuery query,
        bool bounded,
        int knownThrough)
    {
        var pinned = query.PinnedTerms is null
            ? null
            : new HashSet<string>(query.PinnedTerms, StringComparer.OrdinalIgnoreCase);

        var candidates = document.Terms
            .Where(x => !bounded
                || x.FirstSeenChapter is int seen && seen <= knownThrough)
            .Where(x => query.TermFilter?.Invoke(x) != false);

        if (query.RelevantText is string text)
        {
            candidates = candidates
                .Where(x =>
                    pinned?.Contains(x.Source) == true
                    || text.Contains(x.Source, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => pinned?.Contains(x.Source) == true);
        }

        return candidates
            .Take(Math.Max(0, query.MaxTerms))
            .Select(x => new StoryTermView(x.Source, x.Category, x.FirstSeenChapter))
            .ToArray();
    }

    private static string ComputeHash(
        StoryContextQuery query,
        StoryStyleView? style,
        IReadOnlyList<string> themes,
        IReadOnlyList<StoryEntityView> entities,
        IReadOnlyList<StoryTermView> terms,
        IReadOnlyList<StoryChapterView> recent,
        IReadOnlyList<StoryChapterView> earlier)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                scope = query.Scope.ToString(),
                chapter = query.Chapter,
                style,
                themes,
                entities,
                terms,
                recent,
                earlier
            },
            HashOptions);

        return Convert.ToHexString(SHA256.HashData(payload))[..32].ToLowerInvariant();
    }
}

/// <summary>Compact neutral rendering of a snapshot for AI prompts.</summary>
public static class StoryContextRenderer
{
    public static string Render(
        StoryContextSnapshot snapshot,
        int maxCharacters,
        string? heading = null)
    {
        var lines = new List<string>();
        lines.Add(heading ?? snapshot.Scope switch
        {
            StoryContextScope.BeforeChapter => $"STORY CONTEXT (known before chapter {snapshot.Chapter})",
            StoryContextScope.AfterChapter => $"STORY CONTEXT (known after chapter {snapshot.Chapter})",
            _ => "STORY CONTEXT"
        });

        if (snapshot.Style is { } style)
        {
            AddValue(lines, "Narrative perspective", style.NarrativePerspective);
            AddValue(lines, "Overall style", style.OverallStyle);
            AddValue(lines, "Register", style.Register);
            AddValue(lines, "Audience", style.Audience);
        }

        if (snapshot.Themes.Count > 0)
        {
            lines.Add("Themes: " + string.Join(", ", snapshot.Themes));
        }

        if (snapshot.Entities.Count > 0)
        {
            lines.Add("Known characters, places and things:");
            foreach (var entity in snapshot.Entities)
            {
                lines.Add(
                    $"- {entity.Name} [{entity.Type}]"
                    + Optional(entity.Aliases.Count > 0 ? string.Join(", ", entity.Aliases) : null, " aka=")
                    + Optional(entity.Pronouns, " pronouns=")
                    + Optional(entity.Appearance, " appearance=")
                    + Optional(entity.Relationships, " relationships=")
                    + Optional(entity.Description, " notes="));
            }
        }

        if (snapshot.RecentChapters.Count > 0)
        {
            lines.Add("Recent chapters:");
            foreach (var chapter in snapshot.RecentChapters)
            {
                lines.Add(
                    $"- Chapter {chapter.Number}: {chapter.Title}"
                    + Optional(chapter.Summary, " summary=")
                    + Optional(chapter.ContinuityNotes, " continuity="));
            }
        }

        if (snapshot.Terms.Count > 0)
        {
            lines.Add("Terminology: " + string.Join("; ", snapshot.Terms.Select(x => $"{x.Source} [{x.Category}]")));
        }

        if (snapshot.EarlierChapters.Count > 0)
        {
            lines.Add("Earlier chapters:");
            foreach (var chapter in snapshot.EarlierChapters)
            {
                lines.Add($"- {chapter.Number}: {chapter.Title}" + Optional(chapter.Summary, " — "));
            }
        }

        return StoryContextBuilder.FitLines(lines, maxCharacters);
    }

    private static void AddValue(List<string> lines, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(name + ": " + value);
        }
    }

    private static string Optional(string? value, string prefix) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : prefix + value;
}

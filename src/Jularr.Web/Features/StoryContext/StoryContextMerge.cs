namespace Jularr.Web.Features.StoryContext;

/// <summary>
/// Incremental merge rules for <see cref="StoryContextDocument"/>. A chapter
/// extraction only adds or updates what it mentions: unchanged facts are not
/// repeated, entities, aliases and terms are deduplicated, and every value is
/// capped so the document stays compact however long the work gets.
/// </summary>
public static class StoryContextMerge
{
    public const int MaxEntities = 250;
    public const int MaxTerms = 500;
    public const int MaxChapters = 1000;
    public const int MaxThemes = 40;
    public const int MaxAliases = 8;
    public const int MaxHistoryPerEntity = 8;

    public const int NameLength = 240;
    public const int TypeLength = 120;
    public const int DescriptionLength = 1200;
    public const int PronounsLength = 300;
    public const int RelationshipsLength = 1200;
    public const int VoiceNotesLength = 1200;
    public const int AppearanceLength = 800;
    public const int SummaryLength = 2500;
    public const int ContinuityLength = 2000;
    public const int ChapterTitleLength = 500;
    public const int ThemeLength = 160;

    /// <summary>
    /// Applies a book-level analysis. Existing values win (owner edits and
    /// earlier analyses are not overwritten); only missing facts are filled.
    /// Analysis entities get no first-seen chapter: they come from a sample,
    /// so they stay out of spoiler-safe snapshots until a chapter extraction
    /// or text scan places them.
    /// </summary>
    public static bool ApplySeed(
        StoryContextDocument document,
        StorySeedInput seed)
    {
        var style = document.Style;
        var hadAnalysis = !style.IsEmpty || document.Themes.Count > 0;
        var horizonKnown = !hadAnalysis || document.AnalysisThroughChapter is not null;

        var analysisChanged = false;
        analysisChanged |= Fill(() => style.NarrativePerspective, value => style.NarrativePerspective = value, StoryText.Clean(seed.NarrativePerspective, 800));
        analysisChanged |= Fill(() => style.OverallStyle, value => style.OverallStyle = value, StoryText.Clean(seed.OverallStyle, 1600));
        analysisChanged |= Fill(() => style.Register, value => style.Register = value, StoryText.Clean(seed.Register, 800));
        analysisChanged |= Fill(() => style.Audience, value => style.Audience = value, StoryText.Clean(seed.Audience, 800));
        analysisChanged |= MergeThemes(document.Themes, seed.Themes);

        if (analysisChanged)
        {
            document.AnalysisThroughChapter = horizonKnown && seed.AnalysisThroughChapter is int through
                ? Math.Max(document.AnalysisThroughChapter ?? 0, through)
                : null;
        }

        var changed = analysisChanged;

        foreach (var input in seed.Entities)
        {
            var normalized = NormalizeEntity(input);
            if (normalized is null)
            {
                continue;
            }

            var existing = FindEntity(document, normalized.Name, normalized.Type, normalized.Aliases);
            if (existing is null)
            {
                if (document.Entities.Count >= MaxEntities)
                {
                    continue;
                }

                document.Entities.Add(new StoryEntity
                {
                    Name = normalized.Name,
                    Type = normalized.Type!,
                    Aliases = [.. normalized.Aliases ?? []],
                    Description = normalized.Description,
                    Pronouns = normalized.Pronouns,
                    Relationships = normalized.Relationships,
                    VoiceNotes = normalized.VoiceNotes,
                    Appearance = normalized.Appearance,
                    Origin = StoryFactOrigins.Analysis
                });
                changed = true;
                continue;
            }

            changed |= Fill(() => existing.Description, value => existing.Description = value, normalized.Description);
            changed |= Fill(() => existing.Pronouns, value => existing.Pronouns = value, normalized.Pronouns);
            changed |= Fill(() => existing.Relationships, value => existing.Relationships = value, normalized.Relationships);
            changed |= Fill(() => existing.VoiceNotes, value => existing.VoiceNotes = value, normalized.VoiceNotes);
            changed |= Fill(() => existing.Appearance, value => existing.Appearance = value, normalized.Appearance);
            changed |= MergeAliases(existing, normalized.Aliases);
        }

        foreach (var input in seed.Terms)
        {
            var source = StoryText.Clean(input.Source, NameLength);
            if (source is null || FindTerm(document, source) is not null)
            {
                continue;
            }

            if (document.Terms.Count >= MaxTerms)
            {
                continue;
            }

            document.Terms.Add(new StoryTerm
            {
                Source = source,
                Category = StoryText.Clean(input.Category, TypeLength) ?? "term",
                Origin = StoryFactOrigins.Analysis
            });
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Applies what one chapter extraction learned. Re-applying the same
    /// chapter (another language, a retry) replaces that chapter's memory and
    /// merges its facts without duplicating anything.
    /// </summary>
    public static bool ApplyChapter(
        StoryContextDocument document,
        StoryChapterInput input,
        Func<string, bool>? isTermCategoryLocked = null)
    {
        if (input.Number < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Chapter numbers start at 1.");
        }

        var chapterNumber = input.Number;

        foreach (var entityInput in input.Entities)
        {
            var normalized = NormalizeEntity(entityInput);
            if (normalized is null)
            {
                continue;
            }

            var entity = FindEntity(document, normalized.Name, normalized.Type, normalized.Aliases);
            if (entity is null)
            {
                if (document.Entities.Count >= MaxEntities)
                {
                    continue;
                }

                entity = new StoryEntity
                {
                    Name = normalized.Name,
                    Type = normalized.Type!,
                    Origin = StoryFactOrigins.Chapter
                };
                document.Entities.Add(entity);
            }

            entity.Description = StoryText.Prefer(normalized.Description, entity.Description);
            entity.Pronouns = StoryText.Prefer(normalized.Pronouns, entity.Pronouns);
            entity.Relationships = StoryText.Prefer(normalized.Relationships, entity.Relationships);
            entity.VoiceNotes = StoryText.Prefer(normalized.VoiceNotes, entity.VoiceNotes);
            entity.Appearance = StoryText.Prefer(normalized.Appearance, entity.Appearance);
            MergeAliases(entity, [normalized.Name, .. normalized.Aliases ?? []]);
            entity.FirstSeenChapter = entity.FirstSeenChapter is int seen
                ? Math.Min(seen, chapterNumber)
                : chapterNumber;

            if (RecordState(entity, chapterNumber, normalized))
            {
                entity.LastChangedChapter = Math.Max(
                    entity.LastChangedChapter ?? 0,
                    chapterNumber);
            }
        }

        foreach (var termInput in input.Terms)
        {
            var source = StoryText.Clean(termInput.Source, NameLength);
            if (source is null)
            {
                continue;
            }

            var category = StoryText.Clean(termInput.Category, TypeLength) ?? "term";
            var term = FindTerm(document, source);
            if (term is null)
            {
                if (document.Terms.Count >= MaxTerms)
                {
                    continue;
                }

                term = new StoryTerm
                {
                    Source = source,
                    Category = category,
                    Origin = StoryFactOrigins.Chapter
                };
                document.Terms.Add(term);
            }
            else if (isTermCategoryLocked?.Invoke(term.Source) != true)
            {
                term.Category = category;
            }

            term.FirstSeenChapter = term.FirstSeenChapter is int seen
                ? Math.Min(seen, chapterNumber)
                : chapterNumber;
        }

        var memory = new StoryChapterMemory
        {
            ChapterId = input.ChapterId,
            Number = chapterNumber,
            Title = StoryText.Clean(input.Title, ChapterTitleLength) ?? $"Chapter {chapterNumber}",
            Summary = StoryText.Clean(input.Summary, SummaryLength),
            ContinuityNotes = StoryText.Clean(input.ContinuityNotes, ContinuityLength),
            SourceHash = StoryText.Clean(input.SourceHash, 128),
            ExtractedBy = StoryText.Clean(input.ExtractedBy, 80),
            UpdatedAt = DateTime.UtcNow
        };

        document.Chapters.RemoveAll(x =>
            x.ChapterId == input.ChapterId
            || x.Number == chapterNumber);
        document.Chapters.Add(memory);
        return true;
    }

    /// <summary>Owner edit: replaces the neutral facts of one entity.</summary>
    public static StoryEntity UpsertEntity(
        StoryContextDocument document,
        StoryEntityInput input)
    {
        var normalized = NormalizeEntity(input)
            ?? throw new InvalidOperationException(
                "Entity name is required.");

        var entity = FindEntity(document, normalized.Name, normalized.Type, aliases: null);
        if (entity is null)
        {
            if (document.Entities.Count >= MaxEntities)
            {
                throw new InvalidOperationException(
                    "The story context already contains the maximum number of entities.");
            }

            entity = new StoryEntity
            {
                Name = normalized.Name,
                Type = normalized.Type!,
                Origin = StoryFactOrigins.Manual
            };
            document.Entities.Add(entity);
        }

        entity.Description = normalized.Description;
        entity.Pronouns = normalized.Pronouns;
        entity.Relationships = normalized.Relationships;
        entity.VoiceNotes = normalized.VoiceNotes;
        return entity;
    }

    public static bool RemoveEntity(
        StoryContextDocument document,
        string name,
        string type) =>
        document.Entities.RemoveAll(x =>
            x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)
            && x.Type.Equals(type.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;

    public static StoryTerm UpsertTerm(
        StoryContextDocument document,
        string source,
        string? category)
    {
        var cleanSource = StoryText.Clean(source, NameLength)
            ?? throw new InvalidOperationException(
                "Source term is required.");

        var term = FindTerm(document, cleanSource);
        if (term is null)
        {
            if (document.Terms.Count >= MaxTerms)
            {
                throw new InvalidOperationException(
                    "The story context already contains the maximum number of terms.");
            }

            term = new StoryTerm
            {
                Source = cleanSource,
                Origin = StoryFactOrigins.Manual
            };
            document.Terms.Add(term);
        }

        term.Category = StoryText.Clean(category, TypeLength) ?? "term";
        return term;
    }

    public static bool RemoveTerm(
        StoryContextDocument document,
        string source) =>
        document.Terms.RemoveAll(x =>
            x.Source.Equals(source.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;

    public static void SetStyle(
        StoryContextDocument document,
        string? narrativePerspective,
        string? overallStyle,
        string? register,
        string? audience)
    {
        document.Style.NarrativePerspective = StoryText.Clean(narrativePerspective, 800);
        document.Style.OverallStyle = StoryText.Clean(overallStyle, 1600);
        document.Style.Register = StoryText.Clean(register, 800);
        document.Style.Audience = StoryText.Clean(audience, 800);
    }

    public static StoryEntity? FindEntity(
        StoryContextDocument document,
        string name,
        string? type,
        IReadOnlyList<string>? aliases)
    {
        var cleanType = StoryText.Clean(type, TypeLength) ?? "entity";

        var exact = document.Entities.FirstOrDefault(x =>
            x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && x.Type.Equals(cleanType, StringComparison.OrdinalIgnoreCase));
        if (exact is not null || aliases is null)
        {
            return exact;
        }

        // Alias match (neutral extraction only): the same kind of entity
        // already known under another name or one of its aliases.
        var kind = StoryEntityKinds.Classify(cleanType);
        var names = aliases.Append(name).ToArray();
        return document.Entities.FirstOrDefault(x =>
            x.Kind == kind
            && names.Any(candidate =>
                x.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                || x.Aliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)));
    }

    public static StoryTerm? FindTerm(
        StoryContextDocument document,
        string source) =>
        document.Terms.FirstOrDefault(x =>
            x.Source.Equals(source.Trim(), StringComparison.OrdinalIgnoreCase));

    public static void Normalize(StoryContextDocument document)
    {
        document.Version = StoryContextDocument.CurrentVersion;
        document.SourceLanguage = StoryText.NormalizeLanguage(document.SourceLanguage);
        document.Themes.RemoveAll(string.IsNullOrWhiteSpace);
        document.Entities.RemoveAll(x => string.IsNullOrWhiteSpace(x.Name));
        document.Terms.RemoveAll(x => string.IsNullOrWhiteSpace(x.Source));

        TrimTo(document.Themes, MaxThemes);
        TrimTo(document.Entities, MaxEntities);
        TrimTo(document.Terms, MaxTerms);

        document.Chapters.Sort((left, right) => left.Number.CompareTo(right.Number));
        if (document.Chapters.Count > MaxChapters)
        {
            document.Chapters.RemoveRange(0, document.Chapters.Count - MaxChapters);
        }

        foreach (var entity in document.Entities)
        {
            entity.Aliases.RemoveAll(x =>
                string.IsNullOrWhiteSpace(x)
                || x.Equals(entity.Name, StringComparison.OrdinalIgnoreCase));
            TrimTo(entity.Aliases, MaxAliases);
            entity.History.Sort((left, right) => left.Chapter.CompareTo(right.Chapter));

            // Keep the first known state and the most recent ones: early
            // boundaries keep their introduction, later ones stay current.
            while (entity.History.Count > MaxHistoryPerEntity)
            {
                entity.History.RemoveAt(1);
            }
        }
    }

    private static bool RecordState(
        StoryEntity entity,
        int chapter,
        StoryEntityInput incoming)
    {
        var previous = entity.History
            .Where(x => x.Chapter < chapter)
            .MaxBy(x => x.Chapter);
        var atChapter = entity.History.FirstOrDefault(x => x.Chapter == chapter);

        var state = new StoryEntityState
        {
            Chapter = chapter,
            Description = StoryText.Prefer(incoming.Description, StoryText.Prefer(atChapter?.Description, previous?.Description)),
            Pronouns = StoryText.Prefer(incoming.Pronouns, StoryText.Prefer(atChapter?.Pronouns, previous?.Pronouns)),
            Relationships = StoryText.Prefer(incoming.Relationships, StoryText.Prefer(atChapter?.Relationships, previous?.Relationships)),
            Appearance = StoryText.Prefer(incoming.Appearance, StoryText.Prefer(atChapter?.Appearance, previous?.Appearance))
        };

        if (atChapter is not null)
        {
            if (SameFacts(atChapter, state))
            {
                return false;
            }

            entity.History.Remove(atChapter);
            entity.History.Add(state);
            return true;
        }

        // Nothing new compared with what was already known before this
        // chapter: do not store a duplicate state.
        if (previous is not null && SameFacts(previous, state))
        {
            return false;
        }

        if (previous is null
            && state.Description is null
            && state.Pronouns is null
            && state.Relationships is null
            && state.Appearance is null)
        {
            return false;
        }

        entity.History.Add(state);
        return true;
    }

    private static bool SameFacts(StoryEntityState left, StoryEntityState right) =>
        string.Equals(left.Description, right.Description, StringComparison.Ordinal)
        && string.Equals(left.Pronouns, right.Pronouns, StringComparison.Ordinal)
        && string.Equals(left.Relationships, right.Relationships, StringComparison.Ordinal)
        && string.Equals(left.Appearance, right.Appearance, StringComparison.Ordinal);

    private static StoryEntityInput? NormalizeEntity(StoryEntityInput input)
    {
        var name = StoryText.Clean(input.Name, NameLength);
        if (name is null)
        {
            return null;
        }

        var aliases = (input.Aliases ?? [])
            .Select(x => StoryText.Clean(x, NameLength))
            .OfType<string>()
            .Where(x => !x.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxAliases)
            .ToArray();

        return new StoryEntityInput(
            name,
            StoryText.Clean(input.Type, TypeLength) ?? "entity",
            StoryText.Clean(input.Description, DescriptionLength),
            StoryText.Clean(input.Pronouns, PronounsLength),
            StoryText.Clean(input.Relationships, RelationshipsLength),
            StoryText.Clean(input.VoiceNotes, VoiceNotesLength),
            StoryText.Clean(input.Appearance, AppearanceLength),
            aliases);
    }

    private static bool MergeAliases(StoryEntity entity, IReadOnlyList<string>? aliases)
    {
        var changed = false;
        foreach (var alias in aliases ?? [])
        {
            if (entity.Aliases.Count >= MaxAliases)
            {
                break;
            }

            if (alias.Equals(entity.Name, StringComparison.OrdinalIgnoreCase)
                || entity.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            entity.Aliases.Add(alias);
            changed = true;
        }

        return changed;
    }

    private static bool MergeThemes(List<string> destination, IEnumerable<string> incoming)
    {
        var changed = false;
        foreach (var value in incoming)
        {
            if (destination.Count >= MaxThemes)
            {
                break;
            }

            var clean = StoryText.Clean(value, ThemeLength);
            if (clean is null || destination.Contains(clean, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            destination.Add(clean);
            changed = true;
        }

        return changed;
    }

    private static bool Fill(Func<string?> get, Action<string?> set, string? value)
    {
        if (value is null || !string.IsNullOrWhiteSpace(get()))
        {
            return false;
        }

        set(value);
        return true;
    }

    private static void TrimTo<T>(List<T> values, int max)
    {
        if (values.Count > max)
        {
            values.RemoveRange(max, values.Count - max);
        }
    }
}

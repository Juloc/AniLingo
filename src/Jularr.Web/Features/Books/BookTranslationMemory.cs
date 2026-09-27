using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jularr.Web.Features.StoryContext;

namespace Jularr.Web.Features.Books;

public sealed record BookTranslationChapterMemory(
    Guid ChapterId,
    int ChapterNumber,
    string ChapterTitle,
    string? Summary,
    string? ContinuityNotes,
    DateTime UpdatedAt);

/// <summary>
/// The translation bible of one work and target language: a projection of
/// the shared story memory (<see cref="StoryContextDocument"/>) plus the
/// translation-only choices (target names, glossary targets, locks, notes).
/// The same shape was the stored version-1 file format and is still read for
/// migration.
/// </summary>
public sealed record BookTranslationBible
{
    public int Version { get; init; } = 1;
    public Guid WorkId { get; init; }
    public string SourceLanguage { get; init; } = "und";
    public string TargetLanguage { get; init; } = "und";
    public string? NarrativePerspective { get; set; }
    public string? OverallStyle { get; set; }
    public string? Register { get; set; }
    public string? Audience { get; set; }
    public List<string> Themes { get; init; } = [];
    public List<BookTranslationEntity> Entities { get; init; } = [];
    public List<BookTranslationTerm> Terms { get; init; } = [];
    public List<BookTranslationChapterMemory> Chapters { get; init; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Translation-only data stored per work and target language (format version 2).</summary>
public sealed class BookTranslationExtension
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public Guid WorkId { get; set; }
    public string SourceLanguage { get; set; } = "und";
    public string TargetLanguage { get; set; } = "und";
    public List<BookTranslationEntityName> Entities { get; set; } = [];
    public List<BookTranslationTermChoice> Terms { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class BookTranslationEntityName
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "entity";
    public string TargetName { get; set; } = "";
}

public sealed class BookTranslationTermChoice
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Notes { get; set; }
    public bool Locked { get; set; }
}

public sealed class BookTranslationMemoryStore
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxEntities = 250;
    private const int MaxTerms = 500;
    private const string LegacyBackupSuffix = ".v1.bak";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.Ordinal);

    private readonly string rootPath;
    private readonly StoryContextStore story;

    private SemaphoreSlim Gate =>
        Gates.GetOrAdd(
            rootPath,
            _ => new SemaphoreSlim(1, 1));

    /// <summary>Stores the shared story memory in <c>story-context</c> below <paramref name="rootPath"/>.</summary>
    public BookTranslationMemoryStore(string rootPath)
        : this(
            rootPath,
            new StoryContextStore(
                Path.Combine(
                    RequireRoot(rootPath),
                    "story-context")))
    {
    }

    public BookTranslationMemoryStore(
        string rootPath,
        StoryContextStore story)
    {
        this.rootPath = Path.GetFullPath(RequireRoot(rootPath));
        this.story = story;
    }

    public static string DefaultRoot =>
        Path.Combine(
            "/data",
            "books",
            "translation-memory");

    public static BookTranslationMemoryStore FromConfiguration(
        IConfiguration configuration)
    {
        var configured = configuration[
            "Books:Translation:MemoryPath"]?.Trim();

        return new BookTranslationMemoryStore(
            string.IsNullOrWhiteSpace(configured)
                ? DefaultRoot
                : configured,
            StoryContextStore.FromConfiguration(configuration));
    }

    public StoryContextStore StoryContext => story;

    public async Task<BookTranslationBible?> LoadAsync(
        Guid workId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var extension = await LoadExtensionCoreAsync(
                workId,
                targetLanguage,
                cancellationToken);

            if (extension is null)
            {
                return null;
            }

            var document = await story.LoadAsync(workId, cancellationToken);
            return Compose(extension, document);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<BookTranslationBible> GetOrCreateAsync(
        Guid workId,
        string sourceLanguage,
        string targetLanguage,
        Func<CancellationToken, Task<BookTranslationBibleSeed>> seedFactory,
        CancellationToken cancellationToken)
    {
        var existing = await LoadAsync(
            workId,
            targetLanguage,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var seed = await seedFactory(cancellationToken);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var extension = await LoadExtensionCoreAsync(
                    workId,
                    targetLanguage,
                    cancellationToken)
                ?? new BookTranslationExtension
                {
                    WorkId = workId,
                    SourceLanguage = NormalizeLanguage(sourceLanguage),
                    TargetLanguage = NormalizeLanguage(targetLanguage)
                };

            var document = await story.UpdateAsync(
                workId,
                sourceLanguage,
                doc => StoryContextMerge.ApplySeed(
                    doc,
                    ToSeedInput(seed)),
                cancellationToken);

            foreach (var entity in seed.Entities)
            {
                MergeEntityName(extension, entity);
            }

            foreach (var term in seed.Terms)
            {
                MergeTermChoice(extension, term);
            }

            await SaveExtensionCoreAsync(extension, cancellationToken);
            return Compose(extension, document);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task ApplyChapterDeltaAsync(
        BookTranslationBible bible,
        Guid chapterId,
        int chapterNumber,
        string chapterTitle,
        BookTranslationMemoryDelta delta,
        CancellationToken cancellationToken,
        string? sourceHash = null)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var extension = await LoadExtensionCoreAsync(
                    bible.WorkId,
                    bible.TargetLanguage,
                    cancellationToken)
                ?? new BookTranslationExtension
                {
                    WorkId = bible.WorkId,
                    SourceLanguage = NormalizeLanguage(bible.SourceLanguage),
                    TargetLanguage = NormalizeLanguage(bible.TargetLanguage)
                };

            foreach (var entity in delta.Entities)
            {
                MergeEntityName(extension, entity);
            }

            foreach (var term in delta.Terms)
            {
                MergeTermChoice(extension, term);
            }

            var locked = extension.Terms
                .Where(x => x.Locked)
                .Select(x => x.Source)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var document = await story.UpdateAsync(
                bible.WorkId,
                bible.SourceLanguage,
                doc => StoryContextMerge.ApplyChapter(
                    doc,
                    new StoryChapterInput(
                        chapterId,
                        chapterNumber,
                        chapterTitle,
                        delta.ChapterSummary,
                        delta.ContinuityNotes,
                        sourceHash,
                        "translation:" + extension.TargetLanguage,
                        delta.Entities.Select(ToEntityInput).ToArray(),
                        delta.Terms.Select(x => new StoryTermInput(x.Source, x.Category)).ToArray()),
                    locked.Contains),
                cancellationToken);

            await SaveExtensionCoreAsync(extension, cancellationToken);
            CopyInto(Compose(extension, document), bible);
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task<BookTranslationBible?> UpdateOverviewAsync(
        Guid workId,
        string targetLanguage,
        string? narrativePerspective,
        string? overallStyle,
        string? register,
        string? audience,
        CancellationToken cancellationToken) =>
        MutateAsync(
            workId,
            targetLanguage,
            (_, doc) => StoryContextMerge.SetStyle(
                doc,
                narrativePerspective,
                overallStyle,
                register,
                audience),
            cancellationToken);

    public Task<BookTranslationBible?> UpsertTermAsync(
        Guid workId,
        string targetLanguage,
        string source,
        string target,
        string? category,
        string? notes,
        bool locked,
        CancellationToken cancellationToken)
    {
        var cleanSource = Clean(source, 240)
            ?? throw new InvalidOperationException(
                "Source term is required.");
        var cleanTarget = Clean(target, 240)
            ?? throw new InvalidOperationException(
                "Target term is required.");

        return MutateAsync(
            workId,
            targetLanguage,
            (extension, doc) =>
            {
                var index = extension.Terms.FindIndex(x =>
                    x.Source.Equals(
                        cleanSource,
                        StringComparison.OrdinalIgnoreCase));

                if (index < 0 && extension.Terms.Count >= MaxTerms)
                {
                    throw new InvalidOperationException(
                        "The Book Bible already contains the maximum number of terms.");
                }

                StoryContextMerge.UpsertTerm(
                    doc,
                    cleanSource,
                    Clean(category, 120) ?? "term");

                var value = new BookTranslationTermChoice
                {
                    Source = cleanSource,
                    Target = cleanTarget,
                    Notes = Clean(notes, 1000),
                    Locked = locked
                };

                if (index >= 0)
                {
                    extension.Terms[index] = value;
                }
                else
                {
                    extension.Terms.Add(value);
                }
            },
            cancellationToken);
    }

    public async Task<BookTranslationBible?> RemoveTermAsync(
        Guid workId,
        string targetLanguage,
        string source,
        CancellationToken cancellationToken)
    {
        var clean = source.Trim();
        var referencedElsewhere = await IsReferencedByOtherLanguagesAsync(
            workId,
            targetLanguage,
            extension => extension.Terms.Any(x =>
                x.Source.Equals(clean, StringComparison.OrdinalIgnoreCase)),
            cancellationToken);

        return await MutateAsync(
            workId,
            targetLanguage,
            (extension, doc) =>
            {
                extension.Terms.RemoveAll(x =>
                    x.Source.Equals(
                        clean,
                        StringComparison.OrdinalIgnoreCase));

                if (!referencedElsewhere)
                {
                    StoryContextMerge.RemoveTerm(doc, clean);
                }
            },
            cancellationToken);
    }

    public Task<BookTranslationBible?> UpsertEntityAsync(
        Guid workId,
        string targetLanguage,
        string sourceName,
        string targetName,
        string? type,
        string? description,
        string? pronouns,
        string? relationships,
        string? voiceNotes,
        CancellationToken cancellationToken)
    {
        var cleanSource = Clean(sourceName, 240)
            ?? throw new InvalidOperationException(
                "Source entity name is required.");
        var cleanTarget = Clean(targetName, 240)
            ?? throw new InvalidOperationException(
                "Target entity name is required.");
        var cleanType = Clean(type, 120) ?? "entity";

        return MutateAsync(
            workId,
            targetLanguage,
            (extension, doc) =>
            {
                var index = FindEntityName(extension, cleanSource, cleanType);
                if (index < 0 && extension.Entities.Count >= MaxEntities)
                {
                    throw new InvalidOperationException(
                        "The Book Bible already contains the maximum number of entities.");
                }

                StoryContextMerge.UpsertEntity(
                    doc,
                    new StoryEntityInput(
                        cleanSource,
                        cleanType,
                        Clean(description, 1200),
                        Clean(pronouns, 300),
                        Clean(relationships, 1200),
                        Clean(voiceNotes, 1200)));

                var value = new BookTranslationEntityName
                {
                    Name = cleanSource,
                    Type = cleanType,
                    TargetName = cleanTarget
                };

                if (index >= 0)
                {
                    extension.Entities[index] = value;
                }
                else
                {
                    extension.Entities.Add(value);
                }
            },
            cancellationToken);
    }

    public async Task<BookTranslationBible?> RemoveEntityAsync(
        Guid workId,
        string targetLanguage,
        string sourceName,
        string type,
        CancellationToken cancellationToken)
    {
        var cleanName = sourceName.Trim();
        var cleanType = type.Trim();
        var referencedElsewhere = await IsReferencedByOtherLanguagesAsync(
            workId,
            targetLanguage,
            extension => FindEntityName(extension, cleanName, cleanType) >= 0,
            cancellationToken);

        return await MutateAsync(
            workId,
            targetLanguage,
            (extension, doc) =>
            {
                extension.Entities.RemoveAll(x =>
                    x.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase)
                    && x.Type.Equals(cleanType, StringComparison.OrdinalIgnoreCase));

                if (!referencedElsewhere)
                {
                    StoryContextMerge.RemoveEntity(doc, cleanName, cleanType);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Deletes the bible of one target language. The shared story memory is
    /// removed with the last translation that uses it.
    /// </summary>
    public async Task ResetAsync(
        Guid workId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetPath(workId, targetLanguage);
            foreach (var file in new[] { path, path + LegacyBackupSuffix })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            if (EnumerateExtensionFiles(workId).Any())
            {
                return;
            }

            await story.DeleteAsync(workId, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Deletes every translation bible and the shared story memory of a work.</summary>
    public async Task DeleteWorkAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var directory = WorkDirectory(workId);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            await story.DeleteAsync(workId, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Migrates every version-1 bible below the root into the shared story
    /// memory plus a version-2 translation extension. Idempotent; the
    /// original file is kept as <c>&lt;lang&gt;.json.v1.bak</c>.
    /// </summary>
    public async Task<int> MigrateLegacyAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            return 0;
        }

        var migrated = 0;
        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            if (!Guid.TryParseExact(
                    Path.GetFileName(directory),
                    "N",
                    out var workId))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    if (await ReadFileAsync(file, cancellationToken) is { Legacy: { } legacy }
                        && legacy.WorkId == workId)
                    {
                        await MigrateCoreAsync(legacy, file, cancellationToken);
                        migrated++;
                    }
                }
                finally
                {
                    Gate.Release();
                }
            }
        }

        return migrated;
    }

    public static string RenderContext(
        BookTranslationBible bible,
        int maxCharacters = StoryContextBudgets.TranslationFull) =>
        Render(
            bible,
            new StoryContextQuery
            {
                Fields = StoryContextFields.Style
                    | StoryContextFields.Themes
                    | StoryContextFields.Entities
                    | StoryContextFields.EntityDetails
                    | StoryContextFields.Terms
                    | StoryContextFields.RecentChapters,
                MaxThemes = 20,
                MaxEntities = 80,
                MaxTerms = 140,
                RecentChapterCount = 8
            },
            "Established entities/characters:",
            "Established terminology:",
            "Recent chapter memory:",
            includeEntityNotes: true,
            maxCharacters);

    /// <summary>
    /// Translation context for one source segment: only entities and terms
    /// the segment mentions (locked terms always), and the chapters leading
    /// into <paramref name="chapterNumber"/> when given.
    /// </summary>
    public static string RenderRelevantContext(
        BookTranslationBible bible,
        string sourceText,
        int maxCharacters = StoryContextBudgets.TranslationMemory,
        int? chapterNumber = null) =>
        Render(
            bible,
            new StoryContextQuery
            {
                Chapter = chapterNumber,
                Fields = StoryContextFields.Style
                    | StoryContextFields.Themes
                    | StoryContextFields.Entities
                    | StoryContextFields.EntityDetails
                    | StoryContextFields.Terms
                    | StoryContextFields.RecentChapters,
                RelevantText = sourceText,
                PinnedTerms = bible.Terms
                    .Where(x => x.Locked)
                    .Select(x => x.Source)
                    .ToArray(),
                MaxThemes = 12,
                MaxEntities = 24,
                MaxTerms = 48,
                RecentChapterCount = 3
            },
            "Relevant established entities/characters:",
            "Relevant established terminology:",
            "Recent continuity:",
            includeEntityNotes: false,
            maxCharacters);

    private static string Render(
        BookTranslationBible bible,
        StoryContextQuery query,
        string entitiesHeading,
        string termsHeading,
        string chaptersHeading,
        bool includeEntityNotes,
        int maxCharacters)
    {
        var snapshot = StoryContextBuilder.Build(
            ToDocument(bible),
            query);

        var targets = bible.Entities
            .GroupBy(x => (x.SourceName.ToLowerInvariant(), x.Type.ToLowerInvariant()))
            .ToDictionary(x => x.Key, x => x.First().TargetName);
        var terms = bible.Terms
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder();
        builder.AppendLine("BOOK TRANSLATION BIBLE");

        if (snapshot.Style is { } style)
        {
            AppendValue(builder, "Narrative perspective", style.NarrativePerspective);
            AppendValue(builder, "Overall style", style.OverallStyle);
            AppendValue(builder, "Register", style.Register);
            AppendValue(builder, "Audience", style.Audience);
        }

        if (snapshot.Themes.Count > 0)
        {
            builder.AppendLine("Themes: " + string.Join(", ", snapshot.Themes));
        }

        if (snapshot.Entities.Count > 0)
        {
            builder.AppendLine(entitiesHeading);
            foreach (var entity in snapshot.Entities)
            {
                var target = targets.GetValueOrDefault(
                    (entity.Name.ToLowerInvariant(), entity.Type.ToLowerInvariant()),
                    entity.Name);

                builder.AppendLine(
                    $"- {entity.Name} → {target} [{entity.Type}]"
                    + Optional(entity.Pronouns, " pronouns=")
                    + Optional(entity.Relationships, " relationships=")
                    + Optional(entity.VoiceNotes, " voice=")
                    + (includeEntityNotes
                        ? Optional(entity.Description, " notes=")
                        : ""));
            }
        }

        if (snapshot.Terms.Count > 0)
        {
            builder.AppendLine(termsHeading);
            foreach (var view in snapshot.Terms)
            {
                var term = terms[view.Source];
                builder.AppendLine(
                    $"- {term.Source} → {term.Target} [{term.Category}]"
                    + (term.Locked ? " LOCKED" : "")
                    + Optional(term.Notes, " notes="));
            }
        }

        if (snapshot.RecentChapters.Count > 0)
        {
            builder.AppendLine(chaptersHeading);
            foreach (var chapter in snapshot.RecentChapters)
            {
                builder.AppendLine(
                    $"- Chapter {chapter.Number}: {chapter.Title}"
                    + Optional(chapter.Summary, " summary=")
                    + Optional(chapter.ContinuityNotes, " continuity="));
            }
        }

        return StoryContextBuilder.Truncate(
            builder.ToString().Trim(),
            maxCharacters);
    }

    /// <summary>The bible's neutral content as a story document (for the context builder).</summary>
    private static StoryContextDocument ToDocument(BookTranslationBible bible) =>
        new()
        {
            WorkId = bible.WorkId,
            SourceLanguage = bible.SourceLanguage,
            Style = new StoryStyle
            {
                NarrativePerspective = bible.NarrativePerspective,
                OverallStyle = bible.OverallStyle,
                Register = bible.Register,
                Audience = bible.Audience
            },
            Themes = [.. bible.Themes],
            Entities = bible.Entities
                .Select(x => new StoryEntity
                {
                    Name = x.SourceName,
                    Type = x.Type,
                    Aliases = [.. x.Aliases],
                    Description = x.Description,
                    Pronouns = x.Pronouns,
                    Relationships = x.Relationships,
                    VoiceNotes = x.VoiceNotes
                })
                .ToList(),
            Terms = bible.Terms
                .Select(x => new StoryTerm
                {
                    Source = x.Source,
                    Category = x.Category
                })
                .ToList(),
            Chapters = bible.Chapters
                .Select(x => new StoryChapterMemory
                {
                    ChapterId = x.ChapterId,
                    Number = x.ChapterNumber,
                    Title = x.ChapterTitle,
                    Summary = x.Summary,
                    ContinuityNotes = x.ContinuityNotes
                })
                .ToList()
        };

    private static BookTranslationBible Compose(
        BookTranslationExtension extension,
        StoryContextDocument? document)
    {
        document ??= new StoryContextDocument
        {
            WorkId = extension.WorkId,
            SourceLanguage = extension.SourceLanguage
        };

        var names = extension.Entities
            .GroupBy(x => (x.Name.ToLowerInvariant(), x.Type.ToLowerInvariant()))
            .ToDictionary(x => x.Key, x => x.First().TargetName);

        var entities = document.Entities
            .Where(x => names.ContainsKey((x.Name.ToLowerInvariant(), x.Type.ToLowerInvariant())))
            .Select(x => new BookTranslationEntity(
                x.Name,
                names[(x.Name.ToLowerInvariant(), x.Type.ToLowerInvariant())],
                x.Type,
                x.Description,
                x.Pronouns,
                x.Relationships,
                x.VoiceNotes)
            {
                Aliases = x.Aliases.ToArray()
            })
            .ToList();

        var terms = extension.Terms
            .Select(x => new BookTranslationTerm(
                x.Source,
                x.Target,
                StoryContextMerge.FindTerm(document, x.Source)?.Category ?? "term",
                x.Notes,
                x.Locked))
            .ToList();

        return new BookTranslationBible
        {
            Version = BookTranslationExtension.CurrentVersion,
            WorkId = extension.WorkId,
            SourceLanguage = extension.SourceLanguage,
            TargetLanguage = extension.TargetLanguage,
            NarrativePerspective = document.Style.NarrativePerspective,
            OverallStyle = document.Style.OverallStyle,
            Register = document.Style.Register,
            Audience = document.Style.Audience,
            Themes = [.. document.Themes],
            Entities = entities,
            Terms = terms,
            Chapters = document.Chapters
                .OrderBy(x => x.Number)
                .Select(x => new BookTranslationChapterMemory(
                    x.ChapterId,
                    x.Number,
                    x.Title,
                    x.Summary,
                    x.ContinuityNotes,
                    x.UpdatedAt))
                .ToList(),
            UpdatedAt = extension.UpdatedAt > document.UpdatedAt
                ? extension.UpdatedAt
                : document.UpdatedAt
        };
    }

    private static void CopyInto(
        BookTranslationBible source,
        BookTranslationBible destination)
    {
        destination.NarrativePerspective = source.NarrativePerspective;
        destination.OverallStyle = source.OverallStyle;
        destination.Register = source.Register;
        destination.Audience = source.Audience;
        destination.UpdatedAt = source.UpdatedAt;
        Replace(destination.Themes, source.Themes);
        Replace(destination.Entities, source.Entities);
        Replace(destination.Terms, source.Terms);
        Replace(destination.Chapters, source.Chapters);
    }

    private static void Replace<T>(List<T> destination, List<T> source)
    {
        destination.Clear();
        destination.AddRange(source);
    }

    private async Task<BookTranslationBible?> MutateAsync(
        Guid workId,
        string targetLanguage,
        Action<BookTranslationExtension, StoryContextDocument> mutation,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var extension = await LoadExtensionCoreAsync(
                workId,
                targetLanguage,
                cancellationToken);

            if (extension is null)
            {
                return null;
            }

            var document = await story.UpdateAsync(
                workId,
                extension.SourceLanguage,
                doc =>
                {
                    mutation(extension, doc);
                    return true;
                },
                cancellationToken);

            await SaveExtensionCoreAsync(extension, cancellationToken);
            return Compose(extension, document);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<bool> IsReferencedByOtherLanguagesAsync(
        Guid workId,
        string targetLanguage,
        Func<BookTranslationExtension, bool> predicate,
        CancellationToken cancellationToken)
    {
        var current = GetPath(workId, targetLanguage);

        foreach (var file in EnumerateExtensionFiles(workId))
        {
            if (string.Equals(
                    Path.GetFullPath(file),
                    current,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (await ReadFileAsync(file, cancellationToken) is { Extension: { } other }
                && predicate(other))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> EnumerateExtensionFiles(Guid workId)
    {
        var directory = WorkDirectory(workId);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json")
            : [];
    }

    /// <summary>Loads the language extension, migrating a version-1 bible on first read.</summary>
    private async Task<BookTranslationExtension?> LoadExtensionCoreAsync(
        Guid workId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var path = GetPath(workId, targetLanguage);
        var stored = await ReadFileAsync(path, cancellationToken);

        if (stored?.Legacy is { } legacy)
        {
            return await MigrateCoreAsync(legacy, path, cancellationToken);
        }

        return stored?.Extension;
    }

    private async Task<BookTranslationExtension> MigrateCoreAsync(
        BookTranslationBible legacy,
        string path,
        CancellationToken cancellationToken)
    {
        var backup = path + LegacyBackupSuffix;
        if (!File.Exists(backup))
        {
            File.Copy(path, backup);
        }

        var targetLanguage = NormalizeLanguage(legacy.TargetLanguage);

        await story.UpdateAsync(
            legacy.WorkId,
            legacy.SourceLanguage,
            doc =>
            {
                var changed = StoryContextMerge.ApplySeed(
                    doc,
                    ToSeedInput(
                        new BookTranslationBibleSeed(
                            legacy.NarrativePerspective,
                            legacy.OverallStyle,
                            legacy.Register,
                            legacy.Audience,
                            legacy.Themes,
                            legacy.Entities,
                            legacy.Terms)));

                foreach (var entity in doc.Entities.Where(x => x.Origin == StoryFactOrigins.Analysis))
                {
                    entity.Origin = StoryFactOrigins.Legacy;
                }

                foreach (var term in doc.Terms.Where(x => x.Origin == StoryFactOrigins.Analysis))
                {
                    term.Origin = StoryFactOrigins.Legacy;
                }

                foreach (var chapter in legacy.Chapters)
                {
                    if (doc.Chapters.Any(x =>
                            x.ChapterId == chapter.ChapterId
                            || x.Number == chapter.ChapterNumber))
                    {
                        continue;
                    }

                    doc.Chapters.Add(new StoryChapterMemory
                    {
                        ChapterId = chapter.ChapterId,
                        Number = chapter.ChapterNumber,
                        Title = chapter.ChapterTitle,
                        Summary = chapter.Summary,
                        ContinuityNotes = chapter.ContinuityNotes,
                        ExtractedBy = "translation:" + targetLanguage,
                        UpdatedAt = chapter.UpdatedAt
                    });
                    changed = true;
                }

                return changed;
            },
            cancellationToken);

        var extension = new BookTranslationExtension
        {
            WorkId = legacy.WorkId,
            SourceLanguage = NormalizeLanguage(legacy.SourceLanguage),
            TargetLanguage = targetLanguage,
            UpdatedAt = legacy.UpdatedAt
        };

        foreach (var entity in legacy.Entities)
        {
            MergeEntityName(extension, entity);
        }

        foreach (var term in legacy.Terms)
        {
            MergeTermChoice(extension, term);
        }

        await SaveExtensionCoreAsync(extension, cancellationToken, keepUpdatedAt: true);
        return extension;
    }

    private static StorySeedInput ToSeedInput(BookTranslationBibleSeed seed) =>
        new(
            seed.NarrativePerspective,
            seed.OverallStyle,
            seed.Register,
            seed.Audience,
            seed.Themes,
            seed.Entities.Select(ToEntityInput).ToArray(),
            seed.Terms.Select(x => new StoryTermInput(x.Source, x.Category)).ToArray(),
            seed.AnalysisThroughChapter);

    private static StoryEntityInput ToEntityInput(BookTranslationEntity entity) =>
        new(
            entity.SourceName,
            entity.Type,
            entity.Description,
            entity.Pronouns,
            entity.Relationships,
            entity.VoiceNotes,
            Appearance: null,
            entity.Aliases);

    private static void MergeEntityName(
        BookTranslationExtension extension,
        BookTranslationEntity entity)
    {
        var name = Clean(entity.SourceName, 240);
        var target = Clean(entity.TargetName, 240);
        if (name is null || target is null)
        {
            return;
        }

        var type = Clean(entity.Type, 120) ?? "entity";
        var index = FindEntityName(extension, name, type);
        if (index >= 0)
        {
            extension.Entities[index].TargetName = target;
        }
        else if (extension.Entities.Count < MaxEntities)
        {
            extension.Entities.Add(new BookTranslationEntityName
            {
                Name = name,
                Type = type,
                TargetName = target
            });
        }
    }

    /// <summary>A locked term keeps its target; AI suggestions only fill missing notes.</summary>
    private static void MergeTermChoice(
        BookTranslationExtension extension,
        BookTranslationTerm term)
    {
        var source = Clean(term.Source, 240);
        var target = Clean(term.Target, 240);
        if (source is null || target is null)
        {
            return;
        }

        var notes = Clean(term.Notes, 1000);
        var current = extension.Terms.FirstOrDefault(x =>
            x.Source.Equals(source, StringComparison.OrdinalIgnoreCase));

        if (current is null)
        {
            if (extension.Terms.Count < MaxTerms)
            {
                extension.Terms.Add(new BookTranslationTermChoice
                {
                    Source = source,
                    Target = target,
                    Notes = notes,
                    Locked = term.Locked
                });
            }

            return;
        }

        if (current.Locked)
        {
            current.Notes = string.IsNullOrWhiteSpace(current.Notes)
                ? notes
                : current.Notes;
            return;
        }

        current.Target = target;
        current.Notes = notes;
        current.Locked = term.Locked;
    }

    private static int FindEntityName(
        BookTranslationExtension extension,
        string name,
        string type) =>
        extension.Entities.FindIndex(x =>
            x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && x.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    private sealed record StoredFile(
        BookTranslationExtension? Extension,
        BookTranslationBible? Legacy);

    private static async Task<StoredFile?> ReadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0
                || info.Length > MaxBytes)
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            using var json = JsonDocument.Parse(bytes);
            var version = json.RootElement.TryGetProperty("version", out var value)
                && value.ValueKind == JsonValueKind.Number
                    ? value.GetInt32()
                    : 1;

            return version >= BookTranslationExtension.CurrentVersion
                ? new StoredFile(
                    json.RootElement.Deserialize<BookTranslationExtension>(JsonOptions),
                    null)
                : new StoredFile(
                    null,
                    json.RootElement.Deserialize<BookTranslationBible>(JsonOptions));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or FormatException
                or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task SaveExtensionCoreAsync(
        BookTranslationExtension extension,
        CancellationToken cancellationToken,
        bool keepUpdatedAt = false)
    {
        extension.Version = BookTranslationExtension.CurrentVersion;
        extension.Entities.RemoveAll(x =>
            string.IsNullOrWhiteSpace(x.Name)
            || string.IsNullOrWhiteSpace(x.TargetName));
        extension.Terms.RemoveAll(x =>
            string.IsNullOrWhiteSpace(x.Source)
            || string.IsNullOrWhiteSpace(x.Target));
        if (!keepUpdatedAt)
        {
            extension.UpdatedAt = DateTime.UtcNow;
        }

        var path = GetPath(extension.WorkId, extension.TargetLanguage);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Book translation-memory directory is unavailable.");
        Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        await using (var stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                extension,
                JsonOptions,
                cancellationToken);
        }

        if (new FileInfo(temporary).Length > MaxBytes)
        {
            File.Delete(temporary);
            throw new InvalidOperationException(
                "Book translation memory exceeded the 2 MB safety limit.");
        }

        File.Move(
            temporary,
            path,
            overwrite: true);
    }

    private string WorkDirectory(Guid workId) =>
        Path.Combine(
            rootPath,
            workId.ToString("N"));

    private string GetPath(
        Guid workId,
        string targetLanguage)
    {
        var safeLanguage = Regex.Replace(
                NormalizeLanguage(targetLanguage),
                @"[^a-z0-9._-]+",
                "-")
            .Trim('-');

        if (safeLanguage.Length == 0)
        {
            safeLanguage = "und";
        }

        return Path.Combine(
            WorkDirectory(workId),
            safeLanguage + ".json");
    }

    private static string RequireRoot(string rootPath) =>
        string.IsNullOrWhiteSpace(rootPath)
            ? throw new ArgumentException(
                "Translation memory root path is required.",
                nameof(rootPath))
            : rootPath;

    private static string NormalizeLanguage(
        string? value)
    {
        var normalized = value?
            .Trim()
            .ToLowerInvariant();

        return string.IsNullOrWhiteSpace(normalized)
            ? "und"
            : normalized;
    }

    private static string? Clean(
        string? value,
        int maxLength)
    {
        var clean = value?
            .Trim();

        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength];
    }

    private static void AppendValue(
        StringBuilder builder,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine(name + ": " + value);
        }
    }

    private static string Optional(
        string? value,
        string prefix) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : prefix + value;
}

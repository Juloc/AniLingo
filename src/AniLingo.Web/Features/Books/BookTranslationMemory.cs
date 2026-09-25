using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Books;

public sealed record BookTranslationChapterMemory(
    Guid ChapterId,
    int ChapterNumber,
    string ChapterTitle,
    string? Summary,
    string? ContinuityNotes,
    DateTime UpdatedAt);

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

public sealed class BookTranslationMemoryStore
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.Ordinal);

    private readonly string rootPath;

    private SemaphoreSlim Gate =>
        Gates.GetOrAdd(
            rootPath,
            _ => new SemaphoreSlim(1, 1));

    public BookTranslationMemoryStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException(
                "Translation memory root path is required.",
                nameof(rootPath));
        }

        this.rootPath = Path.GetFullPath(rootPath);
    }

    public static string DefaultRoot =>
        Path.Combine(
            "/data",
            "books",
            "translation-memory");

    public async Task<BookTranslationBible?> LoadAsync(
        Guid workId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var path = GetPath(
            workId,
            targetLanguage);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(
                path,
                cancellationToken);
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

        var bible = new BookTranslationBible
        {
            WorkId = workId,
            SourceLanguage = NormalizeLanguage(sourceLanguage),
            TargetLanguage = NormalizeLanguage(targetLanguage),
            NarrativePerspective = Clean(seed.NarrativePerspective, 800),
            OverallStyle = Clean(seed.OverallStyle, 1600),
            Register = Clean(seed.Register, 800),
            Audience = Clean(seed.Audience, 800),
            UpdatedAt = DateTime.UtcNow
        };

        MergeThemes(
            bible.Themes,
            seed.Themes);
        MergeEntities(
            bible.Entities,
            seed.Entities);
        MergeTerms(
            bible.Terms,
            seed.Terms);

        await SaveAsync(
            bible,
            cancellationToken);
        return bible;
    }

    public async Task ApplyChapterDeltaAsync(
        BookTranslationBible bible,
        Guid chapterId,
        int chapterNumber,
        string chapterTitle,
        BookTranslationMemoryDelta delta,
        CancellationToken cancellationToken)
    {
        MergeEntities(
            bible.Entities,
            delta.Entities);
        MergeTerms(
            bible.Terms,
            delta.Terms);

        var chapter = new BookTranslationChapterMemory(
            chapterId,
            chapterNumber,
            Clean(chapterTitle, 500) ?? $"Chapter {chapterNumber}",
            Clean(delta.ChapterSummary, 2500),
            Clean(delta.ContinuityNotes, 2000),
            DateTime.UtcNow);

        var existingIndex = bible.Chapters.FindIndex(x =>
            x.ChapterId == chapterId);

        if (existingIndex >= 0)
        {
            bible.Chapters[existingIndex] = chapter;
        }
        else
        {
            bible.Chapters.Add(chapter);
        }

        if (bible.Chapters.Count > 1000)
        {
            bible.Chapters.RemoveRange(
                0,
                bible.Chapters.Count - 1000);
        }

        bible.UpdatedAt = DateTime.UtcNow;

        await SaveAsync(
            bible,
            cancellationToken);
    }

    public async Task SaveAsync(
        BookTranslationBible bible,
        CancellationToken cancellationToken)
    {
        var path = GetPath(
            bible.WorkId,
            bible.TargetLanguage);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(
                bible,
                path,
                cancellationToken);
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
            bible =>
            {
                bible.NarrativePerspective =
                    Clean(narrativePerspective, 800);
                bible.OverallStyle =
                    Clean(overallStyle, 1600);
                bible.Register =
                    Clean(register, 800);
                bible.Audience =
                    Clean(audience, 800);
            },
            cancellationToken);

    public Task<BookTranslationBible?> UpsertTermAsync(
        Guid workId,
        string targetLanguage,
        string source,
        string target,
        string? category,
        string? notes,
        bool locked,
        CancellationToken cancellationToken) =>
        MutateAsync(
            workId,
            targetLanguage,
            bible =>
            {
                var cleanSource = Clean(source, 240)
                    ?? throw new InvalidOperationException(
                        "Source term is required.");
                var cleanTarget = Clean(target, 240)
                    ?? throw new InvalidOperationException(
                        "Target term is required.");

                var value = new BookTranslationTerm(
                    cleanSource,
                    cleanTarget,
                    Clean(category, 120) ?? "term",
                    Clean(notes, 1000),
                    locked);

                var index = bible.Terms.FindIndex(x =>
                    x.Source.Equals(
                        cleanSource,
                        StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    bible.Terms[index] = value;
                }
                else if (bible.Terms.Count < 500)
                {
                    bible.Terms.Add(value);
                }
                else
                {
                    throw new InvalidOperationException(
                        "The Book Bible already contains the maximum number of terms.");
                }
            },
            cancellationToken);

    public Task<BookTranslationBible?> RemoveTermAsync(
        Guid workId,
        string targetLanguage,
        string source,
        CancellationToken cancellationToken) =>
        MutateAsync(
            workId,
            targetLanguage,
            bible =>
            {
                bible.Terms.RemoveAll(x =>
                    x.Source.Equals(
                        source.Trim(),
                        StringComparison.OrdinalIgnoreCase));
            },
            cancellationToken);

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
        CancellationToken cancellationToken) =>
        MutateAsync(
            workId,
            targetLanguage,
            bible =>
            {
                var cleanSource = Clean(sourceName, 240)
                    ?? throw new InvalidOperationException(
                        "Source entity name is required.");
                var cleanTarget = Clean(targetName, 240)
                    ?? throw new InvalidOperationException(
                        "Target entity name is required.");
                var cleanType = Clean(type, 120) ?? "entity";

                var value = new BookTranslationEntity(
                    cleanSource,
                    cleanTarget,
                    cleanType,
                    Clean(description, 1200),
                    Clean(pronouns, 300),
                    Clean(relationships, 1200),
                    Clean(voiceNotes, 1200));

                var index = bible.Entities.FindIndex(x =>
                    x.SourceName.Equals(
                        cleanSource,
                        StringComparison.OrdinalIgnoreCase)
                    && x.Type.Equals(
                        cleanType,
                        StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    bible.Entities[index] = value;
                }
                else if (bible.Entities.Count < 250)
                {
                    bible.Entities.Add(value);
                }
                else
                {
                    throw new InvalidOperationException(
                        "The Book Bible already contains the maximum number of entities.");
                }
            },
            cancellationToken);

    public Task<BookTranslationBible?> RemoveEntityAsync(
        Guid workId,
        string targetLanguage,
        string sourceName,
        string type,
        CancellationToken cancellationToken) =>
        MutateAsync(
            workId,
            targetLanguage,
            bible =>
            {
                bible.Entities.RemoveAll(x =>
                    x.SourceName.Equals(
                        sourceName.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    && x.Type.Equals(
                        type.Trim(),
                        StringComparison.OrdinalIgnoreCase));
            },
            cancellationToken);

    public async Task ResetAsync(
        Guid workId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var path = GetPath(
            workId,
            targetLanguage);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<BookTranslationBible?> MutateAsync(
        Guid workId,
        string targetLanguage,
        Action<BookTranslationBible> mutation,
        CancellationToken cancellationToken)
    {
        var path = GetPath(
            workId,
            targetLanguage);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var bible = await LoadCoreAsync(
                path,
                cancellationToken);

            if (bible is null)
            {
                return null;
            }

            mutation(bible);
            await SaveCoreAsync(
                bible,
                path,
                cancellationToken);
            return bible;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<BookTranslationBible?> LoadCoreAsync(
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

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

            return await JsonSerializer.DeserializeAsync<BookTranslationBible>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return null;
        }
    }

    private static async Task SaveCoreAsync(
        BookTranslationBible bible,
        string path,
        CancellationToken cancellationToken)
    {
        NormalizeBible(bible);

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
                bible,
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

    public static string RenderContext(
        BookTranslationBible bible,
        int maxCharacters = 12000)
    {
        var builder = new StringBuilder();

        AppendLine(
            builder,
            "BOOK TRANSLATION BIBLE");
        AppendValue(
            builder,
            "Narrative perspective",
            bible.NarrativePerspective);
        AppendValue(
            builder,
            "Overall style",
            bible.OverallStyle);
        AppendValue(
            builder,
            "Register",
            bible.Register);
        AppendValue(
            builder,
            "Audience",
            bible.Audience);

        if (bible.Themes.Count > 0)
        {
            AppendLine(
                builder,
                "Themes: "
                + string.Join(
                    ", ",
                    bible.Themes.Take(20)));
        }

        if (bible.Entities.Count > 0)
        {
            AppendLine(
                builder,
                "Established entities/characters:");

            foreach (var entity in bible.Entities.Take(80))
            {
                AppendLine(
                    builder,
                    $"- {entity.SourceName} → {entity.TargetName} [{entity.Type}]"
                    + Optional(
                        entity.Pronouns,
                        " pronouns=")
                    + Optional(
                        entity.Relationships,
                        " relationships=")
                    + Optional(
                        entity.VoiceNotes,
                        " voice=")
                    + Optional(
                        entity.Description,
                        " notes="));
            }
        }

        if (bible.Terms.Count > 0)
        {
            AppendLine(
                builder,
                "Established terminology:");

            foreach (var term in bible.Terms.Take(140))
            {
                AppendLine(
                    builder,
                    $"- {term.Source} → {term.Target} [{term.Category}]"
                    + (term.Locked ? " LOCKED" : "")
                    + Optional(
                        term.Notes,
                        " notes="));
            }
        }

        var recentChapters = bible.Chapters
            .OrderByDescending(x => x.ChapterNumber)
            .Take(8)
            .OrderBy(x => x.ChapterNumber)
            .ToArray();

        if (recentChapters.Length > 0)
        {
            AppendLine(
                builder,
                "Recent chapter memory:");

            foreach (var chapter in recentChapters)
            {
                AppendLine(
                    builder,
                    $"- Chapter {chapter.ChapterNumber}: {chapter.ChapterTitle}"
                    + Optional(
                        chapter.Summary,
                        " summary=")
                    + Optional(
                        chapter.ContinuityNotes,
                        " continuity="));
            }
        }

        var value = builder
            .ToString()
            .Trim();

        return value.Length <= maxCharacters
            ? value
            : value[..maxCharacters];
    }

    public static string RenderRelevantContext(
        BookTranslationBible bible,
        string sourceText,
        int maxCharacters = 5000)
    {
        var builder = new StringBuilder();

        AppendLine(builder, "BOOK TRANSLATION BIBLE");
        AppendValue(builder, "Narrative perspective", bible.NarrativePerspective);
        AppendValue(builder, "Overall style", bible.OverallStyle);
        AppendValue(builder, "Register", bible.Register);
        AppendValue(builder, "Audience", bible.Audience);

        if (bible.Themes.Count > 0)
        {
            AppendLine(
                builder,
                "Themes: " + string.Join(", ", bible.Themes.Take(12)));
        }

        var relevantEntities = bible.Entities
            .Where(entity =>
                sourceText.Contains(
                    entity.SourceName,
                    StringComparison.OrdinalIgnoreCase))
            .Take(24)
            .ToArray();

        if (relevantEntities.Length > 0)
        {
            AppendLine(builder, "Relevant established entities/characters:");
            foreach (var entity in relevantEntities)
            {
                AppendLine(
                    builder,
                    $"- {entity.SourceName} → {entity.TargetName} [{entity.Type}]"
                    + Optional(entity.Pronouns, " pronouns=")
                    + Optional(entity.Relationships, " relationships=")
                    + Optional(entity.VoiceNotes, " voice="));
            }
        }

        var relevantTerms = bible.Terms
            .Where(term =>
                term.Locked
                || sourceText.Contains(
                    term.Source,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(term => term.Locked)
            .Take(48)
            .ToArray();

        if (relevantTerms.Length > 0)
        {
            AppendLine(builder, "Relevant established terminology:");
            foreach (var term in relevantTerms)
            {
                AppendLine(
                    builder,
                    $"- {term.Source} → {term.Target} [{term.Category}]"
                    + (term.Locked ? " LOCKED" : "")
                    + Optional(term.Notes, " notes="));
            }
        }

        var recentChapters = bible.Chapters
            .OrderByDescending(x => x.ChapterNumber)
            .Take(3)
            .OrderBy(x => x.ChapterNumber)
            .ToArray();

        if (recentChapters.Length > 0)
        {
            AppendLine(builder, "Recent continuity:");
            foreach (var chapter in recentChapters)
            {
                AppendLine(
                    builder,
                    $"- Chapter {chapter.ChapterNumber}: {chapter.ChapterTitle}"
                    + Optional(chapter.Summary, " summary=")
                    + Optional(chapter.ContinuityNotes, " continuity="));
            }
        }

        var value = builder.ToString().Trim();
        return value.Length <= maxCharacters
            ? value
            : value[..maxCharacters];
    }

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
            rootPath,
            workId.ToString("N"),
            safeLanguage + ".json");
    }

    private static void NormalizeBible(
        BookTranslationBible bible)
    {
        bible.NarrativePerspective =
            Clean(
                bible.NarrativePerspective,
                800);
        bible.OverallStyle =
            Clean(
                bible.OverallStyle,
                1600);
        bible.Register =
            Clean(
                bible.Register,
                800);
        bible.Audience =
            Clean(
                bible.Audience,
                800);

        bible.Themes.RemoveAll(x =>
            string.IsNullOrWhiteSpace(x));
        bible.Entities.RemoveAll(x =>
            string.IsNullOrWhiteSpace(x.SourceName)
            || string.IsNullOrWhiteSpace(x.TargetName));
        bible.Terms.RemoveAll(x =>
            string.IsNullOrWhiteSpace(x.Source)
            || string.IsNullOrWhiteSpace(x.Target));

        if (bible.Themes.Count > 40)
        {
            bible.Themes.RemoveRange(
                40,
                bible.Themes.Count - 40);
        }

        if (bible.Entities.Count > 250)
        {
            bible.Entities.RemoveRange(
                250,
                bible.Entities.Count - 250);
        }

        if (bible.Terms.Count > 500)
        {
            bible.Terms.RemoveRange(
                500,
                bible.Terms.Count - 500);
        }

        bible.UpdatedAt = DateTime.UtcNow;
    }

    private static void MergeThemes(
        List<string> destination,
        IEnumerable<string> incoming)
    {
        foreach (var value in incoming)
        {
            var clean = Clean(
                value,
                160);
            if (clean is null
                || destination.Contains(
                    clean,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            destination.Add(clean);

            if (destination.Count >= 40)
            {
                break;
            }
        }
    }

    private static void MergeEntities(
        List<BookTranslationEntity> destination,
        IEnumerable<BookTranslationEntity> incoming)
    {
        foreach (var item in incoming)
        {
            var source = Clean(
                item.SourceName,
                240);
            var target = Clean(
                item.TargetName,
                240);

            if (source is null
                || target is null)
            {
                continue;
            }

            var type = Clean(
                    item.Type,
                    120)
                ?? "entity";

            var normalized = new BookTranslationEntity(
                source,
                target,
                type,
                Clean(
                    item.Description,
                    1200),
                Clean(
                    item.Pronouns,
                    300),
                Clean(
                    item.Relationships,
                    1200),
                Clean(
                    item.VoiceNotes,
                    1200));

            var index = destination.FindIndex(x =>
                x.SourceName.Equals(
                    source,
                    StringComparison.OrdinalIgnoreCase)
                && x.Type.Equals(
                    type,
                    StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                destination[index] = MergeEntity(
                    destination[index],
                    normalized);
            }
            else if (destination.Count < 250)
            {
                destination.Add(normalized);
            }
        }
    }

    private static BookTranslationEntity MergeEntity(
        BookTranslationEntity current,
        BookTranslationEntity incoming) =>
        current with
        {
            TargetName = incoming.TargetName,
            Description = Prefer(
                incoming.Description,
                current.Description),
            Pronouns = Prefer(
                incoming.Pronouns,
                current.Pronouns),
            Relationships = Prefer(
                incoming.Relationships,
                current.Relationships),
            VoiceNotes = Prefer(
                incoming.VoiceNotes,
                current.VoiceNotes)
        };

    private static void MergeTerms(
        List<BookTranslationTerm> destination,
        IEnumerable<BookTranslationTerm> incoming)
    {
        foreach (var item in incoming)
        {
            var source = Clean(
                item.Source,
                240);
            var target = Clean(
                item.Target,
                240);

            if (source is null
                || target is null)
            {
                continue;
            }

            var category = Clean(
                    item.Category,
                    120)
                ?? "term";

            var normalized = new BookTranslationTerm(
                source,
                target,
                category,
                Clean(
                    item.Notes,
                    1000),
                item.Locked);

            var index = destination.FindIndex(x =>
                x.Source.Equals(
                    source,
                    StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                if (destination.Count < 500)
                {
                    destination.Add(normalized);
                }

                continue;
            }

            var current = destination[index];
            if (current.Locked)
            {
                destination[index] = current with
                {
                    Notes = Prefer(
                        current.Notes,
                        normalized.Notes)
                };
                continue;
            }

            destination[index] = normalized with
            {
                Locked = current.Locked
                    || normalized.Locked
            };
        }
    }

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

    private static string? Prefer(
        string? primary,
        string? fallback) =>
        string.IsNullOrWhiteSpace(primary)
            ? fallback
            : primary;

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
            AppendLine(
                builder,
                name + ": " + value);
        }
    }

    private static void AppendLine(
        StringBuilder builder,
        string value)
    {
        builder.AppendLine(value);
    }

    private static string Optional(
        string? value,
        string prefix) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : prefix + value;
}

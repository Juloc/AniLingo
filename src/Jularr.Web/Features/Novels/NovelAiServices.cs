using System.Text;
using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Jularr.Web.Features.Novels;

public interface INovelTranslator
{
    string Id { get; }

    Task<string> TranslateAsync(
        string japaneseText,
        string targetLanguage,
        CancellationToken cancellationToken);
}

public enum NovelTranslationEngine
{
    Ai,
    TranslateGemma
}

public static class NovelTranslationProviders
{
    public const string TranslateGemmaPrefix = "translategemma:";

    /// <summary>
    /// TranslateGemma text is stored under its own track language ("de-gemma"), so every
    /// query for the AI "de" track (offline export, search, counters) excludes it by construction.
    /// </summary>
    public const string TranslateGemmaTrackSuffix = "-gemma";

    public static bool IsTranslateGemma(string providerId) =>
        providerId.StartsWith(TranslateGemmaPrefix, StringComparison.Ordinal);

    public static string TrackLanguage(NovelTranslationEngine engine, string targetLanguage) =>
        engine == NovelTranslationEngine.TranslateGemma
            ? targetLanguage + TranslateGemmaTrackSuffix
            : targetLanguage;
}

public sealed record NovelMappingChapter(int Number, string Title);
public sealed record NovelMappingEpisode(int SeasonNumber, int Number, string Title);

public sealed record NovelMappingSuggestion(
    int ChapterStart,
    int ChapterEnd,
    int SeasonNumber,
    int EpisodeStart,
    int EpisodeEnd,
    string? Label);

public sealed record NovelMappingSuggestionRequest(
    string NovelTitle,
    IReadOnlyList<NovelMappingChapter> Chapters,
    string AnimeTitle,
    IReadOnlyList<NovelMappingEpisode> Episodes);

public interface INovelMappingSuggester
{
    string Id { get; }

    Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
        NovelMappingSuggestionRequest request,
        CancellationToken cancellationToken);
}

public sealed class NovelTranslationService(
    AppDbContext db,
    NovelImportService imports,
    INovelTranslator translator,
    IHttpClientFactory? httpClientFactory = null,
    IConfiguration? configuration = null,
    AiActivityRunner? activityRunner = null,
    CurrentAccountContext? account = null,
    ILogger<NovelTranslationService>? logger = null)
{
    public const int PromptVersion = 1;
    public const int TranslateGemmaPromptVersion = 1;
    private const int MaxChunkCharacters = 6500;
    private static readonly SemaphoreSlim AiGenerateGate = new(1, 1);
    private static readonly SemaphoreSlim TranslateGemmaGenerateGate = new(1, 1);

    public bool TranslateGemmaConfigured =>
        TranslateGemmaOptions.FromConfiguration(configuration, logger) is not null;

    /// <summary>
    /// Provider id a new local translation is stored under (model, endpoint and algorithm
    /// fingerprint), or null when TranslateGemma is not configured. A cached local translation
    /// with another id stays readable but is not reused by generation.
    /// </summary>
    public string? TranslateGemmaProviderId =>
        TranslateGemmaOptions.FromConfiguration(configuration, logger) is { } options
            ? TranslateGemmaNovelTranslator.BuildProviderId(options)
            : null;

    public Task<NovelTranslation?> GetCachedAsync(
        Guid chapterId,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        GetCachedAsync(
            chapterId,
            targetLanguage,
            NovelTranslationEngine.Ai,
            cancellationToken);

    public Task<NovelTranslation?> GetCachedAsync(
        Guid chapterId,
        string targetLanguage,
        NovelTranslationEngine engine,
        CancellationToken cancellationToken)
    {
        var promptVersion = engine == NovelTranslationEngine.TranslateGemma
            ? TranslateGemmaPromptVersion
            : PromptVersion;

        var trackLanguage = NovelTranslationProviders.TrackLanguage(engine, targetLanguage);
        var query =
            from translation in db.NovelTranslations.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on translation.ChapterId equals chapter.Id
            where translation.ChapterId == chapterId &&
                translation.TargetLanguage == trackLanguage &&
                translation.PromptVersion == promptVersion &&
                translation.SourceHash == chapter.SourceHash
            select translation;

        query = engine == NovelTranslationEngine.TranslateGemma
            ? query.Where(x => x.ProviderId.StartsWith(NovelTranslationProviders.TranslateGemmaPrefix))
            : query.Where(x => !x.ProviderId.StartsWith(NovelTranslationProviders.TranslateGemmaPrefix));

        return query
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<NovelTranslation> TranslateChapterAsync(
        Guid chapterId,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        TranslateChapterAsync(
            chapterId,
            targetLanguage,
            NovelTranslationEngine.Ai,
            cancellationToken);

    public async Task<NovelTranslation> TranslateChapterAsync(
        Guid chapterId,
        string targetLanguage,
        NovelTranslationEngine engine,
        CancellationToken cancellationToken)
    {
        var chapter = await imports.DownloadChapterContentAsync(
            chapterId,
            forceRefresh: false,
            cancellationToken);

        return engine switch
        {
            NovelTranslationEngine.TranslateGemma =>
                await TranslateWithTranslateGemmaAsync(chapter, targetLanguage, cancellationToken),
            _ => await TranslateWithAiAsync(chapter, targetLanguage, cancellationToken)
        };
    }

    public static IReadOnlyList<string> ChunkText(string text, int maxCharacters)
    {
        if (maxCharacters < 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length == 0)
        {
            return [];
        }

        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var paragraph in normalized.Split("\n\n"))
        {
            var remaining = paragraph.Trim();
            if (remaining.Length == 0)
            {
                continue;
            }

            while (remaining.Length > maxCharacters)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString().Trim());
                    builder.Clear();
                }

                var split = FindSafeSplit(remaining, maxCharacters);
                chunks.Add(remaining[..split].Trim());
                remaining = remaining[split..].TrimStart();
            }

            if (builder.Length > 0 &&
                builder.Length + 2 + remaining.Length > maxCharacters)
            {
                chunks.Add(builder.ToString().Trim());
                builder.Clear();
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(remaining);
        }

        if (builder.Length > 0)
        {
            chunks.Add(builder.ToString().Trim());
        }

        return chunks;
    }

    private async Task<NovelTranslation> TranslateWithAiAsync(
        NovelChapter chapter,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var cached = await GetExactAiAsync(
            chapter.Id,
            chapter.SourceHash,
            targetLanguage,
            cancellationToken);

        if (cached is not null)
        {
            if (translator is IAiUsageReporter usageReporter)
            {
                usageReporter.RecordCacheHit("novel-chapter-translation");
            }

            return cached;
        }

        await AiGenerateGate.WaitAsync(cancellationToken);
        try
        {
            cached = await GetExactAiAsync(
                chapter.Id,
                chapter.SourceHash,
                targetLanguage,
                cancellationToken);

            if (cached is not null)
            {
                if (translator is IAiUsageReporter usageReporter)
                {
                    usageReporter.RecordCacheHit("novel-chapter-translation");
                }

                return cached;
            }

            var translatedChunks = new List<string>();
            foreach (var chunk in ChunkText(chapter.OriginalText, MaxChunkCharacters))
            {
                var translated = (await translator.TranslateAsync(
                    chunk,
                    targetLanguage,
                    cancellationToken)).Trim();

                if (translated.Length == 0)
                {
                    throw new InvalidOperationException(
                        "AI translation returned an empty chapter segment.");
                }

                translatedChunks.Add(translated);
            }

            var completed = new NovelTranslation
            {
                ChapterId = chapter.Id,
                TargetLanguage = targetLanguage,
                ProviderId = translator.Id,
                PromptVersion = PromptVersion,
                SourceHash = chapter.SourceHash,
                Text = string.Join("\n\n", translatedChunks),
                CreatedAt = DateTime.UtcNow
            };

            db.NovelTranslations.Add(completed);
            await db.SaveChangesAsync(cancellationToken);
            return completed;
        }
        finally
        {
            AiGenerateGate.Release();
        }
    }

    private async Task<NovelTranslation> TranslateWithTranslateGemmaAsync(
        NovelChapter chapter,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var options = TranslateGemmaOptions.FromConfiguration(configuration, logger)
            ?? throw new InvalidOperationException(
                "TranslateGemma is not configured. Set TranslateGemma:Endpoint to an OpenAI-compatible local chat-completions endpoint.");

        if (httpClientFactory is null)
        {
            throw new InvalidOperationException(
                "TranslateGemma requires the configured HTTP client factory.");
        }

        var localTranslator = new TranslateGemmaNovelTranslator(httpClientFactory, options);
        var trackLanguage = NovelTranslationProviders.TrackLanguage(
            NovelTranslationEngine.TranslateGemma,
            targetLanguage);
        var cached = await GetExactTranslateGemmaAsync(
            chapter.Id,
            chapter.SourceHash,
            trackLanguage,
            localTranslator.Id,
            cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await TranslateGemmaGenerateGate.WaitAsync(cancellationToken);
        try
        {
            cached = await GetExactTranslateGemmaAsync(
                chapter.Id,
                chapter.SourceHash,
                trackLanguage,
                localTranslator.Id,
                cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            var paragraphs = NovelTextLayout.SplitParagraphs(chapter.OriginalText);
            if (paragraphs.Count == 0)
            {
                throw new InvalidOperationException(
                    "The chapter contains no text to translate.");
            }

            // Paragraphs finished by an earlier failed run are kept, so a retried job
            // only requests the ones that are still missing.
            var resumeKey = TranslateGemmaResumeCache.Key(
                chapter.Id,
                chapter.SourceHash,
                trackLanguage,
                localTranslator.Id);
            var translatedParagraphs = TranslateGemmaResumeCache.Get(resumeKey, paragraphs.Count);

            try
            {
                await RunTranslateGemmaActivityAsync(
                    options,
                    paragraphs,
                    token => localTranslator.TranslateParagraphsAsync(
                        paragraphs,
                        targetLanguage,
                        translatedParagraphs,
                        token),
                    cancellationToken);
            }
            catch
            {
                TranslateGemmaResumeCache.Save(resumeKey, translatedParagraphs);
                throw;
            }

            if (translatedParagraphs.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    "TranslateGemma did not return a complete paragraph-aligned translation.");
            }

            var completed = new NovelTranslation
            {
                ChapterId = chapter.Id,
                TargetLanguage = trackLanguage,
                ProviderId = localTranslator.Id,
                PromptVersion = TranslateGemmaPromptVersion,
                SourceHash = chapter.SourceHash,
                Text = string.Join("\n\n", translatedParagraphs),
                CreatedAt = DateTime.UtcNow
            };

            db.NovelTranslations.Add(completed);
            await db.SaveChangesAsync(cancellationToken);
            TranslateGemmaResumeCache.Remove(resumeKey);
            return completed;
        }
        finally
        {
            TranslateGemmaGenerateGate.Release();
        }
    }

    /// <summary>
    /// Runs the chapter as one tracked AI activity (live state, progress, retries and one usage
    /// measurement) when the activity infrastructure is registered.
    /// </summary>
    private async Task RunTranslateGemmaActivityAsync(
        TranslateGemmaOptions options,
        IReadOnlyList<string> paragraphs,
        Func<CancellationToken, Task<int>> translate,
        CancellationToken cancellationToken)
    {
        if (activityRunner is null || account is null)
        {
            await translate(cancellationToken);
            return;
        }

        var inputCharacters = paragraphs.Sum(x => x.Length);
        await activityRunner.RunAsync(
            new AiActivityStart(
                account.ProfileId,
                AiOperations.NovelTranslation,
                TranslateGemmaNovelTranslator.ActivityProviderId,
                new AiInvocationOptions(options.Model, null, null, null),
                AiUsageTracker.EstimateTokens(inputCharacters)),
            inputCharacters,
            translate,
            outputCharacters => outputCharacters,
            cancellationToken);
    }

    private Task<NovelTranslation?> GetExactAiAsync(
        Guid chapterId,
        string sourceHash,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        db.NovelTranslations
            .AsNoTracking()
            .Where(x => x.ChapterId == chapterId &&
                x.TargetLanguage == targetLanguage &&
                x.PromptVersion == PromptVersion &&
                x.SourceHash == sourceHash &&
                !x.ProviderId.StartsWith(NovelTranslationProviders.TranslateGemmaPrefix))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<NovelTranslation?> GetExactTranslateGemmaAsync(
        Guid chapterId,
        string sourceHash,
        string trackLanguage,
        string providerId,
        CancellationToken cancellationToken) =>
        db.NovelTranslations
            .AsNoTracking()
            .Where(x => x.ChapterId == chapterId &&
                x.TargetLanguage == trackLanguage &&
                x.ProviderId == providerId &&
                x.PromptVersion == TranslateGemmaPromptVersion &&
                x.SourceHash == sourceHash)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static int FindSafeSplit(string value, int maxCharacters)
    {
        var start = Math.Max(1, maxCharacters - 500);
        for (var index = maxCharacters; index >= start; index--)
        {
            if (value[index - 1] is '\n' or '。' or '！' or '？' or '!' or '?')
            {
                return index;
            }
        }

        return maxCharacters;
    }
}

public sealed record NovelAnimeChoice(
    Guid AnimeId,
    string Title,
    string Provider,
    string ExternalId);

public sealed class NovelMappingService(
    AppDbContext db,
    INovelMappingSuggester suggester)
{
    public Task<List<NovelAnimeMapping>> GetForChapterAsync(
        Guid workId,
        int chapterNumber,
        CancellationToken cancellationToken) =>
        db.NovelAnimeMappings
            .AsNoTracking()
            .Where(x => x.WorkId == workId &&
                x.ChapterStart <= chapterNumber &&
                x.ChapterEnd >= chapterNumber)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.EpisodeStart)
            .ToListAsync(cancellationToken);

    public Task<List<NovelAnimeChoice>> GetAnimeChoicesAsync(
        CancellationToken cancellationToken) =>
        (
            from anime in db.Anime.AsNoTracking()
            join metadata in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadata.AnimeId
            orderby metadata.PreferredTitle
            select new NovelAnimeChoice(
                anime.Id,
                metadata.PreferredTitle,
                metadata.Provider,
                metadata.ExternalId)
        ).ToListAsync(cancellationToken);

    public async Task AddManualAsync(
        Guid workId,
        Guid animeId,
        int chapterStart,
        int chapterEnd,
        int seasonNumber,
        int episodeStart,
        int episodeEnd,
        string? label,
        CancellationToken cancellationToken)
    {
        var target = await GetAnimeTargetAsync(animeId, cancellationToken);
        await ValidateRangeAsync(
            workId,
            animeId,
            chapterStart,
            chapterEnd,
            seasonNumber,
            episodeStart,
            episodeEnd,
            cancellationToken);

        db.NovelAnimeMappings.Add(new NovelAnimeMapping
        {
            WorkId = workId,
            ChapterStart = chapterStart,
            ChapterEnd = chapterEnd,
            AnimeProvider = target.Provider,
            AnimeExternalId = target.ExternalId,
            SeasonNumber = seasonNumber,
            EpisodeStart = episodeStart,
            EpisodeEnd = episodeEnd,
            Label = CleanLabel(label),
            Source = "manual",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SuggestAsync(
        Guid workId,
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == workId, cancellationToken)
            ?? throw new InvalidOperationException("Novel work was not found.");

        var target = await GetAnimeTargetAsync(animeId, cancellationToken);

        var chapters = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .Select(x => new NovelMappingChapter(x.Number, x.Title))
            .ToListAsync(cancellationToken);

        var episodes = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == animeId)
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .Select(x => new NovelMappingEpisode(
                x.SeasonNumber,
                x.Number,
                x.Title))
            .ToListAsync(cancellationToken);

        if (chapters.Count == 0 || episodes.Count == 0)
        {
            throw new InvalidOperationException(
                "Both novel chapters and local anime episodes are required for AI matching.");
        }

        var suggestions = await suggester.SuggestMappingsAsync(
            new NovelMappingSuggestionRequest(
                work.MetadataTitle ?? work.Title,
                chapters,
                target.Title,
                episodes),
            cancellationToken);

        var valid = new List<NovelMappingSuggestion>();
        foreach (var suggestion in suggestions)
        {
            if (suggestion.ChapterStart > suggestion.ChapterEnd ||
                suggestion.EpisodeStart > suggestion.EpisodeEnd)
            {
                continue;
            }

            var chapterRangeValid =
                chapters.Any(x => x.Number == suggestion.ChapterStart) &&
                chapters.Any(x => x.Number == suggestion.ChapterEnd);

            var episodeRangeValid =
                episodes.Any(x => x.SeasonNumber == suggestion.SeasonNumber &&
                    x.Number == suggestion.EpisodeStart) &&
                episodes.Any(x => x.SeasonNumber == suggestion.SeasonNumber &&
                    x.Number == suggestion.EpisodeEnd);

            if (chapterRangeValid && episodeRangeValid)
            {
                valid.Add(suggestion);
            }
        }

        await db.NovelAnimeMappings
            .Where(x => x.WorkId == workId &&
                x.AnimeProvider == target.Provider &&
                x.AnimeExternalId == target.ExternalId &&
                x.Source == "ai")
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var suggestion in valid)
        {
            db.NovelAnimeMappings.Add(new NovelAnimeMapping
            {
                WorkId = workId,
                ChapterStart = suggestion.ChapterStart,
                ChapterEnd = suggestion.ChapterEnd,
                AnimeProvider = target.Provider,
                AnimeExternalId = target.ExternalId,
                SeasonNumber = suggestion.SeasonNumber,
                EpisodeStart = suggestion.EpisodeStart,
                EpisodeEnd = suggestion.EpisodeEnd,
                Label = CleanLabel(suggestion.Label),
                Source = "ai",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return valid.Count;
    }

    public async Task RemoveAsync(
        Guid workId,
        Guid mappingId,
        CancellationToken cancellationToken)
    {
        await db.NovelAnimeMappings
            .Where(x => x.Id == mappingId && x.WorkId == workId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task ValidateRangeAsync(
        Guid workId,
        Guid animeId,
        int chapterStart,
        int chapterEnd,
        int seasonNumber,
        int episodeStart,
        int episodeEnd,
        CancellationToken cancellationToken)
    {
        if (chapterStart <= 0 || chapterStart > chapterEnd ||
            seasonNumber < 0 ||
            episodeStart <= 0 || episodeStart > episodeEnd)
        {
            throw new InvalidOperationException("The mapping range is invalid.");
        }

        var chapterBounds = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Min = group.Min(x => x.Number),
                Max = group.Max(x => x.Number)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (chapterBounds is null ||
            chapterStart < chapterBounds.Min ||
            chapterEnd > chapterBounds.Max)
        {
            throw new InvalidOperationException("The chapter range does not exist.");
        }

        var episodeNumbers = await db.Episodes
            .AsNoTracking()
            .Where(x => x.AnimeId == animeId && x.SeasonNumber == seasonNumber)
            .Select(x => x.Number)
            .ToListAsync(cancellationToken);

        if (!episodeNumbers.Contains(episodeStart) ||
            !episodeNumbers.Contains(episodeEnd))
        {
            throw new InvalidOperationException("The episode range does not exist.");
        }
    }

    private async Task<NovelAnimeChoice> GetAnimeTargetAsync(
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var target = await (
            from anime in db.Anime.AsNoTracking()
            join metadata in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadata.AnimeId
            where anime.Id == animeId
            select new NovelAnimeChoice(
                anime.Id,
                metadata.PreferredTitle,
                metadata.Provider,
                metadata.ExternalId)
        ).SingleOrDefaultAsync(cancellationToken);

        return target ?? throw new InvalidOperationException(
            "Match the local anime to AniList before creating a novel mapping.");
    }

    private static string? CleanLabel(string? label)
    {
        var clean = label?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        return clean.Length <= 200 ? clean : clean[..200].TrimEnd();
    }
}

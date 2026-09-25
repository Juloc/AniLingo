using System.Text;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Novels;

public interface INovelTranslator
{
    string Id { get; }

    Task<string> TranslateAsync(
        string japaneseText,
        string targetLanguage,
        CancellationToken cancellationToken);
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
    NovelService novelService,
    INovelTranslator translator)
{
    public const int PromptVersion = 1;
    private const int MaxChunkCharacters = 6500;
    private static readonly SemaphoreSlim GenerateGate = new(1, 1);

    public Task<NovelTranslation?> GetCachedAsync(
        Guid chapterId,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        (
            from translation in db.NovelTranslations.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on translation.ChapterId equals chapter.Id
            where translation.ChapterId == chapterId &&
                translation.TargetLanguage == targetLanguage &&
                translation.ProviderId == translator.Id &&
                translation.PromptVersion == PromptVersion &&
                translation.SourceHash == chapter.SourceHash
            orderby translation.CreatedAt descending
            select translation
        ).FirstOrDefaultAsync(cancellationToken);

    public async Task<NovelTranslation> TranslateChapterAsync(
        Guid chapterId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var chapter = await novelService.EnsureChapterContentAsync(
            chapterId,
            forceRefresh: false,
            cancellationToken);

        var cached = await GetExactAsync(
            chapter.Id,
            chapter.SourceHash,
            targetLanguage,
            cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        await GenerateGate.WaitAsync(cancellationToken);
        try
        {
            cached = await GetExactAsync(
                chapter.Id,
                chapter.SourceHash,
                targetLanguage,
                cancellationToken);

            if (cached is not null)
            {
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
            GenerateGate.Release();
        }
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

    private Task<NovelTranslation?> GetExactAsync(
        Guid chapterId,
        string sourceHash,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        db.NovelTranslations
            .AsNoTracking()
            .Where(x => x.ChapterId == chapterId &&
                x.TargetLanguage == targetLanguage &&
                x.ProviderId == translator.Id &&
                x.PromptVersion == PromptVersion &&
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

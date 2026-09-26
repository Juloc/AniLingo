using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Novels;

/// <summary>
/// Purpose-built reader chapter projection: the current chapter's text, the
/// current German translation and adjacent chapter ids in one query. It never
/// contains the work's chapter index.
/// </summary>
public sealed record NovelReaderChapter(
    Guid WorkId,
    string WorkTitle,
    string? WorkFormat,
    string SourceProvider,
    string? GenresJson,
    Guid Id,
    int Number,
    string Title,
    string OriginalText,
    string? TranslationText,
    Guid? PreviousChapterId,
    Guid? NextChapterId)
{
    public bool HasContent => OriginalText.Length > 0;
    public bool HasTranslation => !string.IsNullOrWhiteSpace(TranslationText);
}

/// <summary>Small chapter/work identity used by reader write handlers.</summary>
public sealed record NovelChapterContext(
    Guid ChapterId,
    int Number,
    Guid WorkId,
    string WorkTitle,
    string? WorkFormat,
    string SourceProvider,
    string? GenresJson,
    bool HasContent);

public sealed record NovelChapterNavItem(
    Guid Id,
    int Number,
    string Title,
    bool HasContent,
    bool HasTranslation);

public sealed record NovelChapterWindow(
    IReadOnlyList<NovelChapterNavItem> Items,
    bool HasBefore,
    bool HasAfter);

/// <summary>
/// Bounded read projections for the Novel library, work detail and reader.
/// Counts are aggregated in the database; no query loads text or
/// translations of other chapters.
/// </summary>
public sealed class NovelCatalogQueries(AppDbContext db)
{
    public const int MaxChapterWindow = 100;
    private const int ChapterWindowLead = 25;

    public async Task<List<NovelListItem>> GetLibraryAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var works = await db.NovelWorks
            .AsNoTracking()
            .OrderBy(work => work.MetadataTitle ?? work.Title)
            .Select(work => new
            {
                work.Id,
                Title = work.MetadataTitle ?? work.Title,
                work.MetadataNativeTitle,
                work.Author,
                Description = work.MetadataDescription ?? work.Description,
                work.CoverImageUrl,
                work.BannerImageUrl,
                work.MetadataStatus,
                work.MetadataChapterCount,
                work.MetadataVolumeCount,
                ChapterCount = db.NovelChapters.Count(chapter => chapter.WorkId == work.Id),
                LoadedChapterCount = db.NovelChapters.Count(chapter =>
                    chapter.WorkId == work.Id &&
                    chapter.OriginalText != ""),
                TranslatedChapterCount = db.NovelChapters.Count(chapter =>
                    chapter.WorkId == work.Id &&
                    db.NovelTranslations.Any(translation =>
                        translation.ChapterId == chapter.Id &&
                        translation.TargetLanguage == NovelReadingLanguage.German &&
                        translation.PromptVersion == NovelTranslationService.PromptVersion &&
                        translation.SourceHash == chapter.SourceHash))
            })
            .ToListAsync(cancellationToken);

        if (works.Count == 0)
        {
            return [];
        }

        var progress = await (
            from item in db.NovelProgress.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on item.ChapterId equals chapter.Id
            where item.ProfileId == profileId
            select new
            {
                item.WorkId,
                ChapterId = chapter.Id,
                chapter.Number,
                chapter.Title,
                item.PositionPermille,
                item.UpdatedAt
            })
            .ToDictionaryAsync(x => x.WorkId, cancellationToken);

        return works
            .Select(work =>
            {
                progress.TryGetValue(work.Id, out var current);
                return new NovelListItem(
                    work.Id,
                    work.Title,
                    work.MetadataNativeTitle,
                    work.Author,
                    work.Description,
                    work.CoverImageUrl,
                    work.BannerImageUrl,
                    work.MetadataStatus,
                    work.MetadataChapterCount,
                    work.MetadataVolumeCount,
                    work.ChapterCount,
                    work.LoadedChapterCount,
                    work.TranslatedChapterCount,
                    current?.ChapterId,
                    current?.Number,
                    current?.Title,
                    current?.PositionPermille ?? 0,
                    current?.UpdatedAt);
            })
            .ToList();
    }

    public async Task<NovelWorkDetail?> GetWorkDetailAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == workId, cancellationToken);

        if (work is null)
        {
            return null;
        }

        var chapters = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .Select(chapter => new NovelChapterItem(
                chapter.Id,
                chapter.Number,
                chapter.Title,
                chapter.OriginalText != "",
                db.NovelTranslations.Any(translation =>
                    translation.ChapterId == chapter.Id &&
                    translation.TargetLanguage == NovelReadingLanguage.German &&
                    translation.PromptVersion == NovelTranslationService.PromptVersion &&
                    translation.SourceHash == chapter.SourceHash),
                chapter.PublishedAt))
            .ToListAsync(cancellationToken);

        var mappings = await db.NovelAnimeMappings
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.ChapterStart)
            .ThenBy(x => x.EpisodeStart)
            .ToListAsync(cancellationToken);

        return new NovelWorkDetail(work, chapters, mappings);
    }

    public Task<string?> GetWorkTitleAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        db.NovelWorks
            .AsNoTracking()
            .Where(x => x.Id == workId)
            .Select(x => x.MetadataTitle ?? x.Title)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<List<Guid>> GetChapterIdsWithoutContentAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId && x.OriginalText == "")
            .OrderBy(x => x.Number)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

    public Task<NovelReaderChapter?> GetReaderChapterAsync(
        Guid chapterId,
        CancellationToken cancellationToken) =>
        (
            from chapter in db.NovelChapters.AsNoTracking()
            join work in db.NovelWorks.AsNoTracking()
                on chapter.WorkId equals work.Id
            where chapter.Id == chapterId
            select new NovelReaderChapter(
                work.Id,
                work.MetadataTitle ?? work.Title,
                work.Format,
                work.SourceProvider,
                work.MetadataGenresJson,
                chapter.Id,
                chapter.Number,
                chapter.Title,
                chapter.OriginalText,
                db.NovelTranslations
                    .Where(translation =>
                        translation.ChapterId == chapter.Id &&
                        translation.TargetLanguage == NovelReadingLanguage.German &&
                        translation.PromptVersion == NovelTranslationService.PromptVersion &&
                        translation.SourceHash == chapter.SourceHash)
                    .OrderByDescending(translation => translation.CreatedAt)
                    .Select(translation => translation.Text)
                    .FirstOrDefault(),
                db.NovelChapters
                    .Where(previous =>
                        previous.WorkId == chapter.WorkId &&
                        previous.Number < chapter.Number)
                    .OrderByDescending(previous => previous.Number)
                    .Select(previous => (Guid?)previous.Id)
                    .FirstOrDefault(),
                db.NovelChapters
                    .Where(next =>
                        next.WorkId == chapter.WorkId &&
                        next.Number > chapter.Number)
                    .OrderBy(next => next.Number)
                    .Select(next => (Guid?)next.Id)
                    .FirstOrDefault())
        ).SingleOrDefaultAsync(cancellationToken);

    public Task<NovelChapterContext?> GetChapterContextAsync(
        Guid chapterId,
        CancellationToken cancellationToken) =>
        (
            from chapter in db.NovelChapters.AsNoTracking()
            join work in db.NovelWorks.AsNoTracking()
                on chapter.WorkId equals work.Id
            where chapter.Id == chapterId
            select new NovelChapterContext(
                chapter.Id,
                chapter.Number,
                work.Id,
                work.MetadataTitle ?? work.Title,
                work.Format,
                work.SourceProvider,
                work.MetadataGenresJson,
                chapter.OriginalText != "")
        ).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> HasContentAsync(
        Guid chapterId,
        CancellationToken cancellationToken) =>
        db.NovelChapters
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == chapterId && x.OriginalText != "",
                cancellationToken);

    /// <summary>
    /// Bounded chapter navigation for the reader drawer. Without a cursor the
    /// window surrounds <paramref name="anchorNumber"/>; <paramref name="after"/>
    /// and <paramref name="before"/> page forwards/backwards by chapter number.
    /// A search query filters by chapter number or title.
    /// </summary>
    public async Task<NovelChapterWindow> GetChapterWindowAsync(
        Guid workId,
        int anchorNumber,
        string? query,
        int? after,
        int? before,
        int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, MaxChapterWindow);
        var chapters = FilterChapters(workId, query);

        if (after is int afterNumber)
        {
            var page = await Project(chapters
                    .Where(x => x.Number > afterNumber)
                    .OrderBy(x => x.Number)
                    .Take(limit + 1))
                .ToListAsync(cancellationToken);
            return new NovelChapterWindow(page.Take(limit).ToList(), true, page.Count > limit);
        }

        if (before is int beforeNumber)
        {
            var page = await Project(chapters
                    .Where(x => x.Number < beforeNumber)
                    .OrderByDescending(x => x.Number)
                    .Take(limit + 1))
                .ToListAsync(cancellationToken);
            var items = page.Take(limit).Reverse().ToList();
            return new NovelChapterWindow(items, page.Count > limit, true);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var page = await Project(chapters
                    .OrderBy(x => x.Number)
                    .Take(limit + 1))
                .ToListAsync(cancellationToken);
            return new NovelChapterWindow(page.Take(limit).ToList(), false, page.Count > limit);
        }

        var lead = Math.Min(ChapterWindowLead, limit - 1);
        var earlier = await Project(chapters
                .Where(x => x.Number < anchorNumber)
                .OrderByDescending(x => x.Number)
                .Take(lead + 1))
            .ToListAsync(cancellationToken);
        var trailing = limit - Math.Min(lead, earlier.Count);
        var later = await Project(chapters
                .Where(x => x.Number >= anchorNumber)
                .OrderBy(x => x.Number)
                .Take(trailing + 1))
            .ToListAsync(cancellationToken);

        var window = earlier
            .Take(lead)
            .Reverse()
            .Concat(later.Take(trailing))
            .ToList();

        return new NovelChapterWindow(
            window,
            earlier.Count > lead,
            later.Count > trailing);
    }

    private IQueryable<NovelChapter> FilterChapters(Guid workId, string? query)
    {
        var chapters = db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId);

        var needle = query?.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return chapters;
        }

        if (needle.Length > 200)
        {
            needle = needle[..200];
        }

        var pattern = "%" + needle
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

        return int.TryParse(needle, out var number)
            ? chapters.Where(x =>
                x.Number == number ||
                EF.Functions.Like(x.Title, pattern, "\\"))
            : chapters.Where(x => EF.Functions.Like(x.Title, pattern, "\\"));
    }

    private IQueryable<NovelChapterNavItem> Project(IQueryable<NovelChapter> chapters) =>
        chapters.Select(chapter => new NovelChapterNavItem(
            chapter.Id,
            chapter.Number,
            chapter.Title,
            chapter.OriginalText != "",
            db.NovelTranslations.Any(translation =>
                translation.ChapterId == chapter.Id &&
                translation.TargetLanguage == NovelReadingLanguage.German &&
                translation.PromptVersion == NovelTranslationService.PromptVersion &&
                translation.SourceHash == chapter.SourceHash)));
}

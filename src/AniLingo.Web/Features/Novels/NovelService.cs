using System.Security.Cryptography;
using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.ReaderPreferences;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Novels;

public sealed class NovelService(
    AppDbContext db,
    IEnumerable<INovelSourceProvider> sourceProviders)
{
    public async Task<Guid> ImportWorkAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Enter a valid absolute novel URL.");
        }

        var provider = GetProvider(uri);
        var snapshot = await provider.GetWorkAsync(uri, cancellationToken);

        var work = await db.NovelWorks
            .SingleOrDefaultAsync(
                x => x.SourceProvider == snapshot.Provider &&
                    x.SourceKey == snapshot.SourceKey,
                cancellationToken);

        if (work is null)
        {
            work = new NovelWork
            {
                SourceProvider = snapshot.Provider,
                SourceKey = snapshot.SourceKey,
                SourceUrl = snapshot.SourceUrl,
                ImportedAt = DateTime.UtcNow
            };
            db.NovelWorks.Add(work);
        }

        ApplyWorkSnapshot(work, snapshot);

        var existing = await db.NovelChapters
            .Where(x => x.WorkId == work.Id)
            .ToDictionaryAsync(x => x.Number, cancellationToken);

        foreach (var sourceChapter in snapshot.Chapters)
        {
            if (!existing.TryGetValue(sourceChapter.Number, out var chapter))
            {
                chapter = new NovelChapter
                {
                    WorkId = work.Id,
                    Number = sourceChapter.Number,
                    ImportedAt = DateTime.UtcNow
                };
                db.NovelChapters.Add(chapter);
                existing[sourceChapter.Number] = chapter;
            }

            chapter.Title = sourceChapter.Title;
            chapter.SourceUrl = sourceChapter.SourceUrl;
            chapter.PublishedAt = sourceChapter.PublishedAt ?? chapter.PublishedAt;
            chapter.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return work.Id;
    }

    public async Task RefreshWorkAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .SingleOrDefaultAsync(x => x.Id == workId, cancellationToken)
            ?? throw new InvalidOperationException("Novel work was not found.");

        await ImportWorkAsync(work.SourceUrl, cancellationToken);
    }

    public async Task<NovelChapter> EnsureChapterContentAsync(
        Guid chapterId,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var chapter = await db.NovelChapters
            .SingleOrDefaultAsync(x => x.Id == chapterId, cancellationToken)
            ?? throw new InvalidOperationException("Novel chapter was not found.");

        if (!forceRefresh && chapter.HasContent)
        {
            return chapter;
        }

        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleAsync(x => x.Id == chapter.WorkId, cancellationToken);

        var provider = sourceProviders.FirstOrDefault(
            x => x.Key.Equals(work.SourceProvider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Novel source provider '{work.SourceProvider}' is not registered.");

        var snapshot = await provider.GetChapterAsync(
            new Uri(chapter.SourceUrl),
            cancellationToken);

        chapter.Title = snapshot.Title;
        chapter.SourceUrl = snapshot.SourceUrl;
        chapter.OriginalText = snapshot.OriginalText;
        chapter.SourceHash = Hash(snapshot.OriginalText);
        chapter.PublishedAt = snapshot.PublishedAt ?? chapter.PublishedAt;
        chapter.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return chapter;
    }

    public async Task<List<NovelListItem>> GetWorksAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var works = await db.NovelWorks
            .AsNoTracking()
            .OrderBy(x => x.MetadataTitle ?? x.Title)
            .ToListAsync(cancellationToken);

        if (works.Count == 0)
        {
            return [];
        }

        var workIds = works.Select(x => x.Id).ToArray();

        var chapters = await db.NovelChapters
            .AsNoTracking()
            .Where(x => workIds.Contains(x.WorkId))
            .Select(x => new
            {
                x.Id,
                x.WorkId,
                x.Number,
                x.Title,
                x.SourceHash,
                HasContent = x.OriginalText != ""
            })
            .ToListAsync(cancellationToken);

        var translations = await db.NovelTranslations
            .AsNoTracking()
            .Where(x => x.TargetLanguage == "de")
            .Select(x => new { x.ChapterId, x.SourceHash })
            .ToListAsync(cancellationToken);

        var translated = translations
            .Select(x => (x.ChapterId, x.SourceHash))
            .ToHashSet();

        var progresses = await db.NovelProgress
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && workIds.Contains(x.WorkId))
            .ToDictionaryAsync(x => x.WorkId, cancellationToken);

        var chaptersByWork = chapters
            .GroupBy(x => x.WorkId)
            .ToDictionary(x => x.Key, x => x.OrderBy(chapter => chapter.Number).ToArray());

        var result = new List<NovelListItem>(works.Count);

        foreach (var work in works)
        {
            var workChapters = chaptersByWork.GetValueOrDefault(work.Id) ?? [];
            progresses.TryGetValue(work.Id, out var progress);
            var current = progress is null
                ? null
                : workChapters.FirstOrDefault(x => x.Id == progress.ChapterId);

            result.Add(new NovelListItem(
                work.Id,
                work.MetadataTitle ?? work.Title,
                work.MetadataNativeTitle,
                work.Author,
                work.MetadataDescription ?? work.Description,
                work.CoverImageUrl,
                work.BannerImageUrl,
                work.MetadataStatus,
                work.MetadataChapterCount,
                work.MetadataVolumeCount,
                workChapters.Length,
                workChapters.Count(x => x.HasContent),
                workChapters.Count(x => translated.Contains((x.Id, x.SourceHash))),
                current?.Id,
                current?.Number,
                current?.Title,
                progress?.PositionPermille ?? 0,
                progress?.UpdatedAt));
        }

        return result;
    }

    public async Task<NovelWorkDetail?> GetWorkAsync(
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
                db.NovelTranslations.Any(
                    translation => translation.ChapterId == chapter.Id &&
                        translation.TargetLanguage == "de" &&
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

    public async Task<(NovelWork Work, NovelChapter Chapter)?> GetChapterAsync(
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var chapter = await db.NovelChapters
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == chapterId, cancellationToken);

        if (chapter is null)
        {
            return null;
        }

        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleAsync(x => x.Id == chapter.WorkId, cancellationToken);

        return (work, chapter);
    }

    public async Task<(Guid? Previous, Guid? Next)> GetAdjacentChapterIdsAsync(
        Guid workId,
        int chapterNumber,
        CancellationToken cancellationToken)
    {
        var previous = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId && x.Number < chapterNumber)
            .OrderByDescending(x => x.Number)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var next = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId && x.Number > chapterNumber)
            .OrderBy(x => x.Number)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return (previous, next);
    }

    public async Task SaveProgressAsync(
        string profileId,
        Guid workId,
        Guid chapterId,
        int positionPermille,
        string anchorLanguage,
        int? anchorParagraphIndex,
        int anchorOffset,
        CancellationToken cancellationToken)
    {
        var chapter = await db.NovelChapters
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == chapterId && x.WorkId == workId,
                cancellationToken)
            ?? throw new InvalidOperationException("Novel chapter was not found in this work.");

        var language = NormalizeLanguage(anchorLanguage);
        var (paragraphIndex, offset, anchorText) = await ResolveAnchorAsync(
            chapter,
            language,
            anchorParagraphIndex,
            anchorOffset,
            cancellationToken);

        var progress = await db.NovelProgress
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId && x.WorkId == workId,
                cancellationToken);

        if (progress is null)
        {
            progress = new NovelProgress
            {
                ProfileId = profileId,
                WorkId = workId
            };
            db.NovelProgress.Add(progress);
        }

        progress.ChapterId = chapterId;
        progress.PositionPermille = Math.Clamp(positionPermille, 0, 1000);
        progress.AnchorLanguage = language;
        progress.AnchorParagraphIndex = paragraphIndex;
        progress.AnchorOffset = offset;
        progress.AnchorText = anchorText;
        progress.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<NovelProgress?> GetProgressAsync(
        string profileId,
        Guid workId,
        CancellationToken cancellationToken) =>
        db.NovelProgress
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId && x.WorkId == workId,
                cancellationToken);

    public Task<NovelBookmark> AddBookmarkAsync(
        string profileId,
        Guid chapterId,
        int positionPermille,
        string language,
        int? paragraphIndex,
        int characterOffset,
        string? label,
        CancellationToken cancellationToken) =>
        AddBookmarkAsync(
            profileId,
            chapterId,
            positionPermille,
            language,
            paragraphIndex,
            characterOffset,
            label,
            null,
            null,
            cancellationToken);

    public async Task<NovelBookmark> AddBookmarkAsync(
        string profileId,
        Guid chapterId,
        int positionPermille,
        string language,
        int? paragraphIndex,
        int characterOffset,
        string? label,
        string? style,
        string? color,
        CancellationToken cancellationToken)
    {
        var chapter = await db.NovelChapters
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == chapterId, cancellationToken)
            ?? throw new InvalidOperationException("Novel chapter was not found.");

        language = NormalizeLanguage(language);
        var (resolvedParagraph, resolvedOffset, anchorText) = await ResolveAnchorAsync(
            chapter,
            language,
            paragraphIndex,
            characterOffset,
            cancellationToken);

        var bookmark = new NovelBookmark
        {
            ProfileId = profileId,
            WorkId = chapter.WorkId,
            ChapterId = chapter.Id,
            PositionPermille = Math.Clamp(positionPermille, 0, 1000),
            Language = language,
            ParagraphIndex = resolvedParagraph,
            CharacterOffset = resolvedOffset,
            AnchorText = anchorText,
            Label = NormalizeOptional(label, 120),
            Style = ReaderPreferenceRules.NormalizeBookmarkStyle(style),
            Color = ReaderPreferenceRules.NormalizeBookmarkColor(color)
        };

        db.NovelBookmarks.Add(bookmark);
        await db.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    public Task<List<NovelBookmark>> GetBookmarksAsync(
        string profileId,
        Guid workId,
        CancellationToken cancellationToken) =>
        db.NovelBookmarks
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && x.WorkId == workId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<NovelBookmark?> UpdateBookmarkAppearanceAsync(
        string profileId,
        Guid bookmarkId,
        string? style,
        string? color,
        CancellationToken cancellationToken)
    {
        var bookmark = await db.NovelBookmarks
            .SingleOrDefaultAsync(
                x => x.Id == bookmarkId && x.ProfileId == profileId,
                cancellationToken);

        if (bookmark is null)
        {
            return null;
        }

        bookmark.Style = ReaderPreferenceRules.NormalizeBookmarkStyle(style);
        bookmark.Color = ReaderPreferenceRules.NormalizeBookmarkColor(color);
        await db.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    public async Task RemoveBookmarkAsync(
        string profileId,
        Guid bookmarkId,
        CancellationToken cancellationToken)
    {
        var bookmark = await db.NovelBookmarks
            .SingleOrDefaultAsync(
                x => x.Id == bookmarkId && x.ProfileId == profileId,
                cancellationToken);

        if (bookmark is null)
        {
            return;
        }

        db.NovelBookmarks.Remove(bookmark);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<NovelHighlight> AddHighlightAsync(
        string profileId,
        Guid chapterId,
        string language,
        int paragraphIndex,
        int startOffset,
        int endOffset,
        string? note,
        CancellationToken cancellationToken)
    {
        var chapter = await db.NovelChapters
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == chapterId, cancellationToken)
            ?? throw new InvalidOperationException("Novel chapter was not found.");

        language = NormalizeLanguage(language);
        var paragraphs = await GetParagraphsAsync(chapter, language, cancellationToken);

        if (paragraphIndex < 0 || paragraphIndex >= paragraphs.Count)
        {
            throw new InvalidOperationException("The selected paragraph no longer exists.");
        }

        var paragraph = paragraphs[paragraphIndex];
        var start = Math.Clamp(startOffset, 0, paragraph.Length);
        var end = Math.Clamp(endOffset, 0, paragraph.Length);

        if (end <= start)
        {
            throw new InvalidOperationException("Select some text before creating a highlight.");
        }

        if (end - start > 2000)
        {
            throw new InvalidOperationException("A highlight can contain at most 2000 characters.");
        }

        var highlight = new NovelHighlight
        {
            ProfileId = profileId,
            WorkId = chapter.WorkId,
            ChapterId = chapter.Id,
            Language = language,
            ParagraphIndex = paragraphIndex,
            StartOffset = start,
            EndOffset = end,
            Text = paragraph[start..end],
            Note = NormalizeOptional(note, 2000)
        };

        db.NovelHighlights.Add(highlight);
        await db.SaveChangesAsync(cancellationToken);
        return highlight;
    }

    public Task<List<NovelHighlight>> GetHighlightsAsync(
        string profileId,
        Guid workId,
        CancellationToken cancellationToken) =>
        db.NovelHighlights
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && x.WorkId == workId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task RemoveHighlightAsync(
        string profileId,
        Guid highlightId,
        CancellationToken cancellationToken)
    {
        var highlight = await db.NovelHighlights
            .SingleOrDefaultAsync(
                x => x.Id == highlightId && x.ProfileId == profileId,
                cancellationToken);

        if (highlight is null)
        {
            return;
        }

        db.NovelHighlights.Remove(highlight);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(int? ParagraphIndex, int Offset, string? AnchorText)> ResolveAnchorAsync(
        NovelChapter chapter,
        string language,
        int? paragraphIndex,
        int characterOffset,
        CancellationToken cancellationToken)
    {
        if (paragraphIndex is null)
        {
            return (null, 0, null);
        }

        var paragraphs = await GetParagraphsAsync(chapter, language, cancellationToken);
        if (paragraphIndex < 0 || paragraphIndex >= paragraphs.Count)
        {
            return (null, 0, null);
        }

        var paragraph = paragraphs[paragraphIndex.Value];
        return (
            paragraphIndex,
            Math.Clamp(characterOffset, 0, paragraph.Length),
            NovelTextLayout.CreateAnchorText(paragraph));
    }

    private async Task<IReadOnlyList<string>> GetParagraphsAsync(
        NovelChapter chapter,
        string language,
        CancellationToken cancellationToken)
    {
        if (language == "de")
        {
            var translation = await db.NovelTranslations
                .AsNoTracking()
                .Where(x =>
                    x.ChapterId == chapter.Id &&
                    x.TargetLanguage == "de" &&
                    x.SourceHash == chapter.SourceHash)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.Text)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(translation))
            {
                return NovelTextLayout.SplitParagraphs(translation);
            }
        }

        return NovelTextLayout.SplitParagraphs(chapter.OriginalText);
    }

    private INovelSourceProvider GetProvider(Uri sourceUri) =>
        sourceProviders.FirstOrDefault(provider => provider.CanHandle(sourceUri))
        ?? throw new InvalidOperationException(
            "This novel source is not supported. Narou/Ncode URLs are supported first.");

    private static void ApplyWorkSnapshot(
        NovelWork work,
        NovelSourceWorkSnapshot snapshot)
    {
        work.SourceUrl = snapshot.SourceUrl;
        work.Title = snapshot.Title;
        work.Author = snapshot.Author;
        work.Description = snapshot.Description;
        work.UpdatedAt = DateTime.UtcNow;
    }

    private static string NormalizeLanguage(string? language) =>
        string.Equals(language?.Trim(), "de", StringComparison.OrdinalIgnoreCase)
            ? "de"
            : "ja";

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

using System.Security.Cryptography;
using System.Text;
using AniLingo.Web.Data;
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

    public Task<List<NovelListItem>> GetWorksAsync(CancellationToken cancellationToken) =>
        db.NovelWorks
            .AsNoTracking()
            .OrderBy(x => x.Title)
            .Select(work => new NovelListItem(
                work.Id,
                work.MetadataTitle ?? work.Title,
                work.Author,
                work.CoverImageUrl,
                db.NovelChapters.Count(x => x.WorkId == work.Id),
                db.NovelChapters.Count(x => x.WorkId == work.Id && x.OriginalText != ""),
                db.NovelChapters.Count(chapter =>
                    chapter.WorkId == work.Id &&
                    db.NovelTranslations.Any(translation =>
                        translation.ChapterId == chapter.Id &&
                        translation.TargetLanguage == "de" &&
                        translation.SourceHash == chapter.SourceHash))))
            .ToListAsync(cancellationToken);

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
        CancellationToken cancellationToken)
    {
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

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

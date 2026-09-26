using System.Security.Cryptography;
using System.Text;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Novels;

/// <summary>
/// Source/import commands: the only Novel code path that talks to external
/// source providers. Reader and catalog queries never call into this service,
/// so a page render cannot trigger a provider fetch.
/// </summary>
public sealed class NovelImportService(
    AppDbContext db,
    IEnumerable<INovelSourceProvider> sourceProviders,
    NovelMetadataService? metadataService = null)
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

        if (metadataService is not null)
        {
            await metadataService.AutoMatchAsync(
                work.Id,
                cancellationToken);
        }

        return work.Id;
    }

    public async Task RefreshWorkAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var sourceUrl = await db.NovelWorks
            .AsNoTracking()
            .Where(x => x.Id == workId)
            .Select(x => x.SourceUrl)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Novel work was not found.");

        await ImportWorkAsync(sourceUrl, cancellationToken);
    }

    /// <summary>
    /// Downloads chapter text from the work's source provider when it is not
    /// cached yet (or when <paramref name="forceRefresh"/> is set). Callers run
    /// this through Operations, never during a reader GET.
    /// </summary>
    public async Task<NovelChapter> DownloadChapterContentAsync(
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

        var providerKey = await db.NovelWorks
            .AsNoTracking()
            .Where(x => x.Id == chapter.WorkId)
            .Select(x => x.SourceProvider)
            .SingleAsync(cancellationToken);

        var provider = sourceProviders.FirstOrDefault(
            x => x.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Novel source provider '{providerKey}' is not registered.");

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

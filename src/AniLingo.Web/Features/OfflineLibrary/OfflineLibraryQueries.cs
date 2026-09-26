using AniLingo.Web.Data;
using AniLingo.Web.Features.Novels;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.OfflineLibrary;

/// <summary>
/// Builds the canonical offline manifest and chapter payloads from the shared
/// Novel/Book tables (Features/Novels). Read-only: never writes reading state.
/// A chapter's "current" translation of a language is the same one the reader
/// renders (latest cached row whose <c>SourceHash</c> matches the chapter's),
/// so the offline package and the live reader never disagree.
/// </summary>
public sealed class OfflineLibraryQueries(AppDbContext db, NovelVolumeAssetStore assets)
{
    public async Task<OfflineLibraryManifest?> GetManifestAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .AsNoTracking()
            .Where(x => x.Id == workId)
            .Select(x => new { x.Title, x.MetadataTitle, x.Author, x.MetadataDescription, x.Description, x.CoverImageUrl })
            .SingleOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            return null;
        }

        var volumes = await db.NovelVolumes
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .Select(x => new { x.Id, x.Number, x.Title, x.Kind, x.CoverAsset })
            .ToListAsync(cancellationToken);

        var chapters = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .Select(x => new { x.Id, x.VolumeId, x.Number, x.Title, x.SourceHash, HasContent = x.OriginalText != "" })
            .ToListAsync(cancellationToken);

        var currentTranslations = await CurrentTranslationsAsync(
            chapters.Select(x => x.Id),
            cancellationToken);

        var title = work.MetadataTitle ?? work.Title;
        var chapterRefs = new List<OfflineLibraryChapterRef>(chapters.Count);
        var orderedHashes = new List<string>(chapters.Count);

        foreach (var chapter in chapters)
        {
            var translations = currentTranslations.TryGetValue(chapter.Id, out var list)
                ? list
                : [];
            var hash = OfflineLibraryContract.ComputeChapterHash(
                chapter.SourceHash,
                translations.Select(x => (x.TargetLanguage, x.ProviderId, x.PromptVersion, x.SourceHash)));

            chapterRefs.Add(new OfflineLibraryChapterRef(
                chapter.Id,
                chapter.VolumeId,
                chapter.Number,
                chapter.Title,
                hash,
                chapter.HasContent,
                translations.Count > 0));
            orderedHashes.Add(hash);
        }

        var firstVolumeCover = volumes.FirstOrDefault(x => x.CoverAsset is not null);
        var coverAssetUrl = work.CoverImageUrl
            ?? (firstVolumeCover is null
                ? null
                : assets.Resolve(firstVolumeCover.Id, firstVolumeCover.CoverAsset) is null
                    ? null
                    : NovelVolumeAssetStore.Url(firstVolumeCover.Id, firstVolumeCover.CoverAsset!));

        return new OfflineLibraryManifest(
            workId,
            OfflineLibraryContract.SchemaVersion,
            OfflineLibraryContract.ComputeWorkContentVersion(title, work.Author, coverAssetUrl, orderedHashes),
            title,
            work.Author,
            work.MetadataDescription ?? work.Description,
            coverAssetUrl,
            DateTime.UtcNow,
            volumes.Select(x => new OfflineLibraryVolume(
                x.Id,
                x.Number,
                x.Title,
                x.Kind,
                x.CoverAsset is null ? null : NovelVolumeAssetStore.Url(x.Id, x.CoverAsset))).ToArray(),
            chapterRefs);
    }

    public async Task<OfflineLibraryChapterPayload?> GetChapterPayloadAsync(
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var chapter = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.Id == chapterId)
            .Select(x => new
            {
                x.Id,
                x.WorkId,
                x.VolumeId,
                x.Number,
                x.Title,
                x.OriginalText,
                x.ContentJson,
                x.SourceHash
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (chapter is null)
        {
            return null;
        }

        var translations = await db.NovelTranslations
            .AsNoTracking()
            .Where(x => x.ChapterId == chapterId && x.SourceHash == chapter.SourceHash)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.TargetLanguage, x.ProviderId, x.PromptVersion, x.SourceHash, x.Text, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var currentPerLanguage = translations
            .GroupBy(x => x.TargetLanguage, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.CreatedAt).First())
            .ToArray();

        var hash = OfflineLibraryContract.ComputeChapterHash(
            chapter.SourceHash,
            currentPerLanguage.Select(x => (x.TargetLanguage, x.ProviderId, x.PromptVersion, x.SourceHash)));

        var blocks = NovelChapterDocument.Deserialize(chapter.ContentJson)
            .Select(block => new OfflineLibraryContentBlock(
                block.Kind,
                block.Level,
                block.Runs?.Select(run => new OfflineLibraryInlineRun(run.Text, run.Ruby, run.Emphasis, run.Strong)).ToArray(),
                block.Kind == NovelContentBlock.ImageKind && !string.IsNullOrWhiteSpace(block.Source)
                    ? NovelVolumeAssetStore.Url(chapter.VolumeId, block.Source)
                    : null,
                block.Alt))
            .ToArray();

        return new OfflineLibraryChapterPayload(
            chapter.Id,
            chapter.WorkId,
            chapter.VolumeId,
            chapter.Number,
            chapter.Title,
            hash,
            chapter.OriginalText,
            blocks,
            currentPerLanguage
                .Select(x => new OfflineLibraryTranslation(x.TargetLanguage, x.Text))
                .ToArray());
    }

    /// <summary>
    /// Resolves an asset request to an on-disk path, or null when the volume
    /// id or file name is unsafe/unknown. <see cref="NovelVolumeAssetStore"/>
    /// only accepts content-addressed (hash.ext) file names, so a request can
    /// never escape the volume's asset directory.
    /// </summary>
    public string? ResolveAssetPath(Guid volumeId, string asset) =>
        assets.Resolve(volumeId, asset);

    private async Task<Dictionary<Guid, List<(string TargetLanguage, string ProviderId, int PromptVersion, string SourceHash)>>>
        CurrentTranslationsAsync(IEnumerable<Guid> chapterIds, CancellationToken cancellationToken)
    {
        var ids = chapterIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var rows = await (
            from chapter in db.NovelChapters.AsNoTracking()
            join translation in db.NovelTranslations.AsNoTracking()
                on chapter.Id equals translation.ChapterId
            where ids.Contains(chapter.Id) && translation.SourceHash == chapter.SourceHash
            select new
            {
                ChapterId = chapter.Id,
                translation.TargetLanguage,
                translation.ProviderId,
                translation.PromptVersion,
                translation.SourceHash,
                translation.CreatedAt
            }).ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.ChapterId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(x => x.TargetLanguage, StringComparer.OrdinalIgnoreCase)
                    .Select(languageGroup => languageGroup.OrderByDescending(x => x.CreatedAt).First())
                    .Select(x => (x.TargetLanguage, x.ProviderId, x.PromptVersion, x.SourceHash))
                    .ToList());
    }
}

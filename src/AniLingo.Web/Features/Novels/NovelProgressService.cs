using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Novels;

/// <summary>
/// Canonical per-profile reading progress for a novel work: one row per
/// profile and work, anchored to a paragraph of the current chapter.
/// </summary>
public sealed class NovelProgressService(AppDbContext db)
{
    public async Task SaveProgressAsync(
        string profileId,
        Guid chapterId,
        int positionPermille,
        string? anchorLanguage,
        int? anchorParagraphIndex,
        int anchorOffset,
        CancellationToken cancellationToken)
    {
        var language = NovelReadingLanguage.Normalize(anchorLanguage);
        var text = await NovelChapterText.LoadAsync(
            db,
            chapterId,
            language,
            cancellationToken)
            ?? throw new InvalidOperationException("Novel chapter was not found.");

        var (paragraphIndex, offset, anchorText) = NovelChapterText.ResolveAnchor(
            text.Paragraphs,
            anchorParagraphIndex,
            anchorOffset);

        var progress = await db.NovelProgress
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId && x.WorkId == text.WorkId,
                cancellationToken);

        if (progress is null)
        {
            progress = new NovelProgress
            {
                ProfileId = profileId,
                WorkId = text.WorkId
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
}

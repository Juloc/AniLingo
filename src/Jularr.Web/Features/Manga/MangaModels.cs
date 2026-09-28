namespace Jularr.Web.Features.Manga;

public sealed record MangaSeriesItem(
    Guid Id,
    string Title,
    string? NativeTitle,
    string? CoverImageUrl,
    int ChapterCount,
    Guid? PreviewChapterId,
    Guid? CurrentChapterId,
    double? CurrentChapterNumber,
    int CurrentPageIndex,
    int CurrentPageCount,
    DateTime? LastReadAt)
{
    public bool HasProgress => CurrentChapterId is not null;
    public int ProgressPercent =>
        CurrentPageCount <= 0
            ? 0
            : Math.Clamp((int)Math.Round((CurrentPageIndex + 1) * 100d / CurrentPageCount), 0, 100);
}

public sealed record MangaSeriesDetail(
    Guid Id,
    string Title,
    string? NativeTitle,
    string? Description,
    string? CoverImageUrl,
    string? BannerImageUrl,
    string? Status,
    string Direction,
    string SourcePath,
    IReadOnlyList<MangaChapterItem> Chapters);

public sealed record MangaChapterItem(
    Guid Id,
    Guid SeriesId,
    double Number,
    int? VolumeNumber,
    string Title,
    int PageCount,
    string SourceKind,
    DateTime SourceUpdatedAt,
    Guid? VolumeId = null);

/// <summary>
/// A Manga volume's own durable identity (#563): stable independent of any one chapter file, so
/// grouping/ordering by volume survives a chapter rename/reorganize the same way the chapter's own
/// id does. <see cref="Number"/> together with the series is the natural key a rescan matches on.
/// </summary>
public sealed record MangaVolumeItem(
    Guid Id,
    Guid SeriesId,
    int Number,
    string? Title,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record MangaChapterRead(
    Guid Id,
    Guid SeriesId,
    string SeriesTitle,
    double Number,
    int? VolumeNumber,
    string Title,
    int PageCount,
    string Direction);

public sealed record MangaPageItem(
    Guid ChapterId,
    int PageIndex,
    string CachedPath,
    string MimeType);

public sealed record MangaProgressItem(
    Guid SeriesId,
    Guid ChapterId,
    int PageIndex,
    DateTime UpdatedAt);

public sealed record MangaBookmarkItem(
    Guid Id,
    Guid SeriesId,
    Guid ChapterId,
    int PageIndex,
    string? Label,
    DateTime CreatedAt);

public sealed record MangaAniListCandidate(
    string ExternalId,
    string Title,
    string? NativeTitle,
    string? Description,
    string? CoverImageUrl,
    string? BannerImageUrl,
    string? Status,
    string? Format = null,
    int? ChapterCount = null,
    int? VolumeCount = null,
    int? StartYear = null);

public sealed record MangaAutoMatchSource(
    Guid SeriesId,
    string Title,
    string? MetadataExternalId);

public sealed record MangaAniListProgressContext(
    Guid SeriesId,
    string Title,
    string? MetadataExternalId,
    double ChapterNumber,
    int? VolumeNumber,
    int PageIndex,
    int PageCount);

/// <summary>Where a series lives: its id, title and canonical source folder.</summary>
public sealed record MangaSeriesLocation(
    Guid Id,
    string Title,
    string SourcePath);

public sealed record MangaImportResult(
    Guid SeriesId,
    int ChapterCount,
    int PageCount,
    int UpdatedChapterCount);

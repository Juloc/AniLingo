namespace AniLingo.Web.Features.Manga;

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
    DateTime SourceUpdatedAt);

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
    string? Status);

public sealed record MangaImportResult(
    Guid SeriesId,
    int ChapterCount,
    int PageCount,
    int UpdatedChapterCount);

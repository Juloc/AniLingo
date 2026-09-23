namespace AniLingo.Web.Features.Novels;

public sealed class NovelWork
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceProvider { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }

    public string? MetadataProvider { get; set; }
    public string? MetadataExternalId { get; set; }
    public string? MetadataTitle { get; set; }
    public string? MetadataNativeTitle { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Format { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class NovelChapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkId { get; set; }
    public int Number { get; set; }
    public string SourceUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public DateTime? PublishedAt { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool HasContent => OriginalText.Length > 0;
}

public sealed class NovelTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChapterId { get; set; }
    public string TargetLanguage { get; set; } = "de";
    public string ProviderId { get; set; } = "";
    public int PromptVersion { get; set; }
    public string SourceHash { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class NovelProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = "";
    public Guid WorkId { get; set; }
    public Guid ChapterId { get; set; }
    public int PositionPermille { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class NovelAnimeMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkId { get; set; }
    public int ChapterStart { get; set; }
    public int ChapterEnd { get; set; }
    public string AnimeProvider { get; set; } = "";
    public string AnimeExternalId { get; set; } = "";
    public int SeasonNumber { get; set; }
    public int EpisodeStart { get; set; }
    public int EpisodeEnd { get; set; }
    public string? Label { get; set; }
    public string Source { get; set; } = "manual";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record NovelSourceChapterReference(
    int Number,
    string Title,
    string SourceUrl,
    DateTime? PublishedAt = null);

public sealed record NovelSourceWorkSnapshot(
    string Provider,
    string SourceKey,
    string SourceUrl,
    string Title,
    string? Author,
    string? Description,
    IReadOnlyList<NovelSourceChapterReference> Chapters);

public sealed record NovelSourceChapterSnapshot(
    int Number,
    string Title,
    string SourceUrl,
    string OriginalText,
    DateTime? PublishedAt = null);

public interface INovelSourceProvider
{
    string Key { get; }
    bool CanHandle(Uri sourceUri);

    Task<NovelSourceWorkSnapshot> GetWorkAsync(
        Uri sourceUri,
        CancellationToken cancellationToken);

    Task<NovelSourceChapterSnapshot> GetChapterAsync(
        Uri sourceUri,
        CancellationToken cancellationToken);
}

public sealed record NovelListItem(
    Guid Id,
    string Title,
    string? Author,
    string? CoverImageUrl,
    int ChapterCount,
    int LoadedChapterCount,
    int TranslatedChapterCount);

public sealed record NovelChapterItem(
    Guid Id,
    int Number,
    string Title,
    bool HasContent,
    bool HasTranslation,
    DateTime? PublishedAt);

public sealed record NovelWorkDetail(
    NovelWork Work,
    IReadOnlyList<NovelChapterItem> Chapters,
    IReadOnlyList<NovelAnimeMapping> Mappings);

public sealed record NovelMetadataCandidate(
    string Provider,
    string ExternalId,
    string PreferredTitle,
    string? NativeTitle,
    string? CoverImageUrl,
    string? Format,
    string? Status,
    int? ChapterCount,
    int? VolumeCount);

public interface INovelMetadataProvider
{
    string Key { get; }

    Task<IReadOnlyList<NovelMetadataCandidate>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<NovelMetadataCandidate?> GetAsync(
        string externalId,
        CancellationToken cancellationToken);
}

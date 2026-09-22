namespace AniLingo.Web.Features.Metadata;

public sealed class AnimeMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnimeId { get; set; }
    public string Provider { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string PreferredTitle { get; set; } = "";
    public string? RomajiTitle { get; set; }
    public string? EnglishTitle { get; set; }
    public string? NativeTitle { get; set; }
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? BannerImageUrl { get; set; }
    public string? Format { get; set; }
    public string? Status { get; set; }
    public string? Season { get; set; }
    public int? SeasonYear { get; set; }
    public int? EpisodeCount { get; set; }
    public int? EpisodeDurationMinutes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record AnimeMetadataCandidate(
    string Provider,
    string ExternalId,
    string PreferredTitle,
    string? RomajiTitle,
    string? EnglishTitle,
    string? NativeTitle,
    string? Description,
    string? CoverImageUrl,
    string? BannerImageUrl,
    string? Format,
    string? Status,
    string? Season,
    int? SeasonYear,
    int? EpisodeCount,
    int? EpisodeDurationMinutes);

public sealed record AnimeMetadataMatchResult(
    bool Success,
    string? Error = null);

public interface IAnimeMetadataProvider
{
    string Key { get; }

    Task<IReadOnlyList<AnimeMetadataCandidate>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<AnimeMetadataCandidate?> GetAsync(
        string externalId,
        CancellationToken cancellationToken);
}

public static class AnimeMetadataTitles
{
    public static string Choose(
        string? english,
        string? romaji,
        string? native,
        string fallback) =>
        FirstNonEmpty(english, romaji, native, fallback) ?? fallback;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

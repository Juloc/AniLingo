namespace AniLingo.Web.Features.Discovery;

public enum DiscoveryCategory
{
    All,
    Anime,
    LightNovel,
    Manga,
    Book
}

public enum DiscoveryMode
{
    Trending,
    Top,
    MyList,
    Search
}

public sealed record DiscoveryRequest(
    string Query,
    DiscoveryCategory Category,
    DiscoveryMode Mode)
{
    public static DiscoveryRequest Parse(
        string? query,
        string? category,
        string? mode)
    {
        var normalizedQuery = NormalizeQuery(query);
        var normalizedCategory = ParseCategory(category);
        var normalizedMode = normalizedQuery.Length > 0
            ? DiscoveryMode.Search
            : ParseMode(mode);

        return new DiscoveryRequest(
            normalizedQuery,
            normalizedCategory,
            normalizedMode);
    }

    public bool RequiresPersonalAniListAccount =>
        Mode == DiscoveryMode.MyList;

    public string CacheKey(string profileId) =>
        string.Join(
            '|',
            profileId,
            Mode.ToString().ToLowerInvariant(),
            Category.ToString().ToLowerInvariant(),
            Query.ToLowerInvariant());

    public static string NormalizeQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var parts = value
            .Trim()
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);

        return string.Join(" ", parts)
            .Truncate(120);
    }

    private static DiscoveryCategory ParseCategory(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "anime" => DiscoveryCategory.Anime,
            "novel" or "novels" or "lightnovel" or "light-novel" or "light-novels" =>
                DiscoveryCategory.LightNovel,
            "manga" => DiscoveryCategory.Manga,
            "book" or "books" => DiscoveryCategory.Book,
            _ => DiscoveryCategory.All
        };

    private static DiscoveryMode ParseMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "top" or "popular" => DiscoveryMode.Top,
            "my" or "my-list" or "mylist" => DiscoveryMode.MyList,
            _ => DiscoveryMode.Trending
        };
}

public sealed record DiscoveryItem(
    string Id,
    string Category,
    string Provider,
    string ExternalId,
    string Title,
    string? NativeTitle,
    string? Description,
    string? CoverImageUrl,
    string? Format,
    string? Status,
    int? Year,
    int? Progress,
    int? TotalProgress,
    int? VolumeCount,
    string? ListStatus,
    IReadOnlyList<string> Genres,
    bool IsLocal,
    string? LocalUrl,
    string DetailsUrl,
    bool CanImportSource);

public sealed record DiscoveryResponse(
    string Query,
    string Category,
    string Mode,
    bool AniListConnected,
    IReadOnlyList<DiscoveryItem> Items,
    IReadOnlyList<string> Warnings);

internal static class DiscoveryStringExtensions
{
    public static string Truncate(this string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

using System.Security.Cryptography;
using System.Text;

namespace Jularr.Web.Features.Watchlist;

public enum WatchlistMediaType
{
    Anime,
    Tv,
    Movie,
    Manga,
    LightNovel,
    Book
}

public enum WatchPreferenceState
{
    Follow,
    Ignore
}

public static class WatchlistMediaTypeNames
{
    public static string ToStorage(WatchlistMediaType type) => type switch
    {
        WatchlistMediaType.LightNovel => "lightNovel",
        _ => type.ToString().ToLowerInvariant()
    };

    public static WatchlistMediaType? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "anime" => WatchlistMediaType.Anime,
        "tv" or "series" => WatchlistMediaType.Tv,
        "movie" or "movies" or "film" => WatchlistMediaType.Movie,
        "manga" => WatchlistMediaType.Manga,
        "lightnovel" or "light-novel" or "light_novel" or "novel" => WatchlistMediaType.LightNovel,
        "book" or "books" => WatchlistMediaType.Book,
        _ => null
    };

    public static string ToCategory(WatchlistMediaType type) => type switch
    {
        WatchlistMediaType.LightNovel => "light-novel",
        WatchlistMediaType.Movie => "movie",
        WatchlistMediaType.Tv => "tv",
        _ => type.ToString().ToLowerInvariant()
    };
}

public sealed record WatchlistIdentity(
    WatchlistMediaType MediaType,
    string Provider,
    string ExternalId)
{
    public string ProviderKey => Provider.Trim().ToLowerInvariant();

    public string ExternalKey => ExternalId.Trim();

    public string Key => $"{WatchlistMediaTypeNames.ToStorage(MediaType)}:{ProviderKey}:{ExternalKey}";

    public Guid StableId
    {
        get
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Key));
            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }
    }
}

public sealed record WatchlistDraft(
    WatchlistIdentity Identity,
    string Title,
    string? NativeTitle = null,
    string? CoverImageUrl = null,
    string? Format = null,
    string? Status = null,
    int? Year = null,
    Guid? LocalMediaId = null,
    string? DetailsUrl = null);

public sealed record WatchlistItem(
    WatchlistIdentity Identity,
    string Title,
    string? NativeTitle,
    string? CoverImageUrl,
    string? Format,
    string? Status,
    int? Year,
    Guid? LocalMediaId,
    string? DetailsUrl,
    Guid? FranchiseId,
    string? FranchiseTitle,
    bool IsExplicit)
{
    public Guid StableId => LocalMediaId ?? Identity.StableId;

    public bool IsFromFranchise => FranchiseId is not null;
}

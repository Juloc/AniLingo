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


public static class WatchlistDraftInput
{
    public static bool TryCreate(
        string? mediaType,
        string? provider,
        string? externalId,
        string? title,
        string? nativeTitle,
        string? coverImageUrl,
        string? format,
        string? status,
        int? year,
        Guid? localMediaId,
        string? detailsUrl,
        out WatchlistDraft draft)
    {
        draft = null!;
        var type = WatchlistMediaTypeNames.Parse(mediaType);
        var normalizedProvider = provider?.Trim().ToLowerInvariant();
        var normalizedId = externalId?.Trim();
        var normalizedTitle = title?.Trim();

        if (type is null ||
            string.IsNullOrWhiteSpace(normalizedProvider) ||
            normalizedProvider.Length > 80 ||
            string.IsNullOrWhiteSpace(normalizedId) ||
            normalizedId.Length > 200 ||
            string.IsNullOrWhiteSpace(normalizedTitle) ||
            normalizedTitle.Length > 500 ||
            year is < 1800 or > 3000)
        {
            return false;
        }

        draft = new WatchlistDraft(
            new WatchlistIdentity(type.Value, normalizedProvider, normalizedId),
            normalizedTitle,
            Limit(nativeTitle, 500),
            SafeUrl(coverImageUrl),
            Limit(format, 80),
            Limit(status, 80),
            year,
            localMediaId,
            SafeUrl(detailsUrl));
        return true;
    }

    public static bool TryIdentity(
        string? mediaType,
        string? provider,
        string? externalId,
        out WatchlistIdentity identity)
    {
        identity = null!;
        var type = WatchlistMediaTypeNames.Parse(mediaType);
        var normalizedProvider = provider?.Trim().ToLowerInvariant();
        var normalizedId = externalId?.Trim();
        if (type is null ||
            string.IsNullOrWhiteSpace(normalizedProvider) ||
            normalizedProvider.Length > 80 ||
            string.IsNullOrWhiteSpace(normalizedId) ||
            normalizedId.Length > 200)
        {
            return false;
        }

        identity = new WatchlistIdentity(type.Value, normalizedProvider, normalizedId);
        return true;
    }

    private static string? Limit(string? value, int max)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null ? null : normalized[..Math.Min(max, normalized.Length)];
    }

    private static string? SafeUrl(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            return null;
        }

        if (normalized.StartsWith('/') && !normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return normalized.Length <= 2048 ? normalized : null;
        }

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
               uri.Scheme is "https" or "http" &&
               normalized.Length <= 2048
            ? normalized
            : null;
    }
}

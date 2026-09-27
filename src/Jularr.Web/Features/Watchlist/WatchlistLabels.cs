using System.Globalization;

namespace Jularr.Web.Features.Watchlist;

/// <summary>Display helpers for followed works; provider values are never shown raw.</summary>
public static class WatchlistLabels
{
    /// <summary>
    /// The UI key of an AniList format, or null when the media type already says it (MANGA,
    /// NOVEL) or the format is unknown.
    /// </summary>
    public static string? FormatKey(string? format) => format?.Trim().ToUpperInvariant() switch
    {
        "TV" => "watchlist.format.tv",
        "TV_SHORT" => "watchlist.format.tvShort",
        "MOVIE" => "watchlist.format.movie",
        "SPECIAL" => "watchlist.format.special",
        "OVA" => "watchlist.format.ova",
        "ONA" => "watchlist.format.ona",
        "MUSIC" => "watchlist.format.music",
        "ONE_SHOT" => "watchlist.format.oneShot",
        _ => null
    };

    /// <summary>The first letter of a title, for a cover placeholder.</summary>
    public static string Initial(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? ""
            : StringInfo.GetNextTextElement(title.Trim(), 0);
}

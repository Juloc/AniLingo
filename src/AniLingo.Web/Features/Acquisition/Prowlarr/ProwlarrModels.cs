using System.Text.Json.Serialization;
using AniLingo.Web.Features.Acquisition;

namespace AniLingo.Web.Features.Acquisition.Prowlarr;

public sealed record ProwlarrSettings(
    string BaseUrl,
    int[] Categories,
    int[] IndexerIds,
    int SearchLimit)
{
    public static ProwlarrSettings CreateDefault(string baseUrl) =>
        new(
            baseUrl,
            Categories: [5000, 5070],
            IndexerIds: [],
            SearchLimit: 100);
}

public sealed record ProwlarrConnection(
    ProwlarrSettings Settings,
    [property: JsonIgnore] string ApiKey);

public sealed record ProwlarrConnectionTestResult(
    bool Success,
    string? Version = null,
    string? Error = null);

public enum ProwlarrAnimeSearchMode
{
    Anime,
    Episode,
    Season
}

public sealed record ProwlarrAnimeSearchTarget(
    string CanonicalTitle,
    IReadOnlyList<string> Aliases,
    ProwlarrAnimeSearchMode Mode,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    int? AbsoluteEpisodeNumber = null);

public sealed record ProwlarrSearchQuery(string Query);

public sealed record ProwlarrSearchWarning(
    string Query,
    string Message);

public sealed record ProwlarrReleaseCandidate(
    string Title,
    string? Indexer,
    int? IndexerId,
    string? Protocol,
    long? SizeBytes,
    int? Seeders,
    int? Leechers,
    DateTimeOffset? PublishedAt,
    int? AgeDays,
    double? AgeHours,
    string? Guid,
    string? InfoUrl,
    AnimeReleaseInfo ParsedRelease,
    IReadOnlyList<string> MatchedQueries,
    [property: JsonIgnore] Uri? InternalDownloadUri,
    [property: JsonIgnore] string? InternalMagnetUri)
{
    public string Identity =>
        !string.IsNullOrWhiteSpace(Guid)
            ? $"prowlarr:{IndexerId?.ToString() ?? "unknown"}:{Guid}"
            : $"release:{IndexerId?.ToString() ?? "unknown"}:{ParsedRelease.ReleaseKey}";
}

public sealed record ProwlarrAnimeSearchResult(
    IReadOnlyList<ProwlarrReleaseCandidate> Releases,
    IReadOnlyList<ProwlarrSearchWarning> Warnings);

public sealed class ProwlarrException(string message, Exception? innerException = null)
    : Exception(message, innerException);

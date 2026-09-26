using System.Text.Json.Serialization;
using AniLingo.Web.Features.Acquisition.Prowlarr;

namespace AniLingo.Web.Features.Acquisition.Indexers;

/// <summary>
/// Kind of indexer connection. Prowlarr aggregates other indexers itself;
/// Newznab is a direct connection to a single usenet indexer. Jularr is
/// usenet-only: torrent indexers (Torznab) are intentionally unsupported.
/// </summary>
public enum IndexerType
{
    Prowlarr,
    Newznab
}

/// <summary>
/// One canonical indexer connection's settings. <see cref="IndexerIds"/> is
/// only meaningful for <see cref="IndexerType.Prowlarr"/> (Prowlarr's own
/// per-indexer restriction); Newznab ignores it.
/// </summary>
public sealed record IndexerSettings(
    string BaseUrl,
    int[] Categories,
    int[] IndexerIds,
    int SearchLimit)
{
    public static IndexerSettings CreateDefault(string baseUrl, IndexerType type) =>
        new(
            baseUrl,
            Categories: type == IndexerType.Prowlarr ? [5000, 5070] : [5070],
            IndexerIds: [],
            SearchLimit: 100);
}

/// <summary>
/// One entry of the canonical indexer list. The API key is protected at
/// rest; <see cref="Priority"/> is lower-is-first, matching download client
/// priority.
/// </summary>
public sealed record IndexerEntry(
    Guid Id,
    string Name,
    IndexerType Type,
    bool Enabled,
    int Priority,
    IndexerSettings Settings,
    [property: JsonIgnore] string ApiKey);

public sealed record IndexerConnectionTestResult(
    bool Success,
    string? Version = null,
    string? Error = null);

public enum IndexerAnimeSearchMode
{
    Anime,
    Episode,
    Season
}

public sealed record IndexerAnimeSearchTarget(
    string CanonicalTitle,
    IReadOnlyList<string> Aliases,
    IndexerAnimeSearchMode Mode,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    int? AbsoluteEpisodeNumber = null);

public sealed record IndexerSearchQuery(string Query);

public sealed record IndexerSearchWarning(
    string IndexerName,
    string Query,
    string Message);

public sealed record IndexerAnimeSearchResult(
    IReadOnlyList<ProwlarrReleaseCandidate> Releases,
    IReadOnlyList<IndexerSearchWarning> Warnings);

/// <summary>
/// One indexer implementation. <see cref="ProwlarrReleaseCandidate"/> is the
/// one release-candidate model every indexer type and the quality scorer
/// share; it already carries a per-result <c>Protocol</c> (usenet/torrent).
/// </summary>
public interface IIndexer
{
    IndexerType Type { get; }

    Task<IndexerConnectionTestResult> TestAsync(
        IndexerEntry entry,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProwlarrReleaseCandidate>> SearchAsync(
        IndexerEntry entry,
        IndexerSearchQuery query,
        CancellationToken cancellationToken);
}

public sealed class IndexerException(string message, Exception? innerException = null)
    : Exception(message, innerException);

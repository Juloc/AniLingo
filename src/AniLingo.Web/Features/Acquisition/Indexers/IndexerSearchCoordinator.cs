using AniLingo.Web.Features.Acquisition.Health;
using AniLingo.Web.Features.Acquisition.Prowlarr;

namespace AniLingo.Web.Features.Acquisition.Indexers;

/// <summary>
/// Searches every enabled, healthy indexer entry (Prowlarr and direct
/// Newznab alike) for an anime/episode/season target and merges the
/// results the same way the single-connection Prowlarr search used to:
/// de-duplicated by release identity, best first. Unhealthy entries are
/// skipped and reported as a warning with the reason.
/// </summary>
public sealed class IndexerSearchCoordinator(
    IReadOnlyDictionary<IndexerType, IIndexer> indexers,
    IndexerStore store,
    AcquisitionHealthStore health,
    ILogger<IndexerSearchCoordinator> logger)
{
    public async Task<bool> HasEnabledIndexerAsync(CancellationToken cancellationToken) =>
        (await store.LoadAllAsync(cancellationToken)).Any(entry => entry.Enabled);

    /// <summary>The ids of every currently enabled indexer entry (Prowlarr and direct
    /// Newznab alike), for callers that need to know whether a Guid-based restriction
    /// (see <paramref name="allowedEntryIds"/> on <see cref="SearchAsync"/>) would leave anything
    /// to search before committing to a search.</summary>
    public async Task<IReadOnlyList<Guid>> EnabledEntryIdsAsync(CancellationToken cancellationToken) =>
        (await store.LoadAllAsync(cancellationToken))
            .Where(entry => entry.Enabled)
            .Select(entry => entry.Id)
            .ToArray();

    public async Task<IndexerAnimeSearchResult> SearchAsync(
        IndexerAnimeSearchTarget target,
        CancellationToken cancellationToken,
        IReadOnlyList<int>? prowlarrIndexerIdOverride = null,
        IReadOnlyCollection<Guid>? allowedEntryIds = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        var entries = (await store.LoadAllAsync(cancellationToken))
            .Where(entry => entry.Enabled && (allowedEntryIds is null || allowedEntryIds.Contains(entry.Id)))
            .OrderBy(entry => entry.Priority)
            .ToArray();

        var queries = IndexerSearchPlanner.Build(target);
        var warnings = new List<IndexerSearchWarning>();
        var aggregated = new Dictionary<string, AggregatedCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!await health.IsHealthyAsync(AcquisitionHealthKind.Indexer, entry.Id, cancellationToken))
            {
                var status = await health.GetAsync(AcquisitionHealthKind.Indexer, entry.Id, cancellationToken);
                var reason = status?.LastError ?? "unhealthy";
                logger.LogWarning("Skipped indexer '{Indexer}': {Reason}", entry.Name, reason);
                warnings.Add(new IndexerSearchWarning(entry.Name, string.Empty, $"Skipped: {reason}"));
                continue;
            }

            if (!indexers.TryGetValue(entry.Type, out var indexer))
            {
                continue;
            }

            var effectiveEntry = entry.Type == IndexerType.Prowlarr && prowlarrIndexerIdOverride is { Count: > 0 }
                ? entry with { Settings = entry.Settings with { IndexerIds = prowlarrIndexerIdOverride.ToArray() } }
                : entry;

            foreach (var query in queries)
            {
                try
                {
                    var candidates = await indexer.SearchAsync(effectiveEntry, query, cancellationToken);
                    foreach (var candidate in candidates)
                    {
                        if (aggregated.TryGetValue(candidate.Identity, out var existing))
                        {
                            existing.AddQuery(query.Query);
                            continue;
                        }

                        aggregated[candidate.Identity] = new AggregatedCandidate(candidate, query.Query);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is IndexerException or ProwlarrException or HttpRequestException or TaskCanceledException)
                {
                    warnings.Add(new IndexerSearchWarning(entry.Name, query.Query, exception.Message));
                }
            }
        }

        var releases = aggregated.Values
            .Select(value => value.Build())
            .OrderByDescending(value => value.Seeders ?? -1)
            .ThenByDescending(value => value.PublishedAt)
            .ThenBy(value => value.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new IndexerAnimeSearchResult(releases, warnings);
    }

    private sealed class AggregatedCandidate
    {
        private readonly ProwlarrReleaseCandidate candidate;
        private readonly HashSet<string> queries = new(StringComparer.OrdinalIgnoreCase);

        public AggregatedCandidate(ProwlarrReleaseCandidate candidate, string query)
        {
            this.candidate = candidate;
            AddQuery(query);
        }

        public void AddQuery(string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
            {
                queries.Add(query);
            }
        }

        public ProwlarrReleaseCandidate Build() =>
            candidate with { MatchedQueries = queries.Order(StringComparer.OrdinalIgnoreCase).ToArray() };
    }
}

namespace AniLingo.Web.Features.Acquisition.Prowlarr;

public sealed class ProwlarrAnimeSearchService(IProwlarrClient client)
{
    public async Task<ProwlarrAnimeSearchResult> SearchAsync(
        ProwlarrConnection connection,
        ProwlarrAnimeSearchTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(target);

        var queries = ProwlarrSearchPlanner.Build(target);
        var warnings = new List<ProwlarrSearchWarning>();
        var aggregated = new Dictionary<string, AggregatedCandidate>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var query in queries)
        {
            try
            {
                var candidates = await client.SearchAsync(
                    connection,
                    query,
                    cancellationToken);

                foreach (var candidate in candidates)
                {
                    if (aggregated.TryGetValue(candidate.Identity, out var existing))
                    {
                        existing.AddQuery(query.Query);
                        continue;
                    }

                    aggregated[candidate.Identity] =
                        new AggregatedCandidate(candidate, query.Query);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ProwlarrException exception)
            {
                warnings.Add(
                    new ProwlarrSearchWarning(
                        query.Query,
                        exception.Message));
            }
        }

        var releases = aggregated.Values
            .Select(value => value.Build())
            .OrderByDescending(value => value.Seeders ?? -1)
            .ThenByDescending(value => value.PublishedAt)
            .ThenBy(value => value.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProwlarrAnimeSearchResult(releases, warnings);
    }

    private sealed class AggregatedCandidate
    {
        private readonly ProwlarrReleaseCandidate candidate;
        private readonly HashSet<string> queries =
            new(StringComparer.OrdinalIgnoreCase);

        public AggregatedCandidate(
            ProwlarrReleaseCandidate candidate,
            string query)
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
            candidate with
            {
                MatchedQueries = queries
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
    }
}

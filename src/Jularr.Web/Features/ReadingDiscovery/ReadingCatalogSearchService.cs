using Jularr.Web.Features.Novels;
using Jularr.Web.Features.ReadingSources;

namespace Jularr.Web.Features.ReadingDiscovery;

/// <summary>AniList as a Light Novel catalog source: published editions, acquired through requests.</summary>
public sealed class AniListCatalogProvider(NovelAniListProvider aniList) : IReadingCatalogProvider
{
    public string Key => NovelAniListProvider.ProviderKey;

    public async Task<IReadOnlyList<ReadingCatalogCandidate>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var results = await aniList.SearchReadingMediaAsync(
            query,
            Math.Clamp(limit, 1, 24),
            includeNovels: true,
            includeManga: false,
            cancellationToken);

        return results.Select(ReadingCatalogSearch.MapAniList).ToArray();
    }
}

/// <summary>
/// The one manual Light Novel search: it asks every enabled source at once, isolates each
/// source's failure, and returns a single ranked list. A source that is slow, unavailable,
/// rate limited or blocking never fails the search; it is reported as unavailable and left
/// alone for a cooldown (see <see cref="ReadingSourceHealthTracker"/>).
/// </summary>
public sealed class ReadingCatalogSearchService(
    IEnumerable<IReadingCatalogProvider> providers,
    ReadingSourceHealthTracker health,
    ILogger<ReadingCatalogSearchService> logger)
{
    /// <summary>Upper bound for one source; the slowest source must not stall the list.</summary>
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public async Task<ReadingCatalogSearchOutcome> SearchLightNovelsAsync(
        ReadingSourceSettingsState sourceSettings,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceSettings);

        var normalized = ReadingCatalogSearch.NormalizeQuery(query);
        if (normalized.Length == 0)
        {
            return ReadingCatalogSearchOutcome.Empty;
        }

        var boundedLimit = Math.Clamp(limit, 1, 24);
        var enabled = providers
            .Where(provider =>
                ReadingSourceCatalog.TryGet(provider.Key, out _) &&
                sourceSettings.IsEnabled(provider.Key))
            .DistinctBy(provider => provider.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var outcomes = await Task.WhenAll(
            enabled.Select(provider => SearchOneAsync(
                provider,
                normalized,
                boundedLimit,
                cancellationToken)));

        // Results are identified by source and id only. A published edition and a web novel
        // with the same title stay separate rows so the user picks the exact source.
        var merged = outcomes
            .SelectMany(outcome => outcome.Candidates)
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        return new ReadingCatalogSearchOutcome(
            ReadingCatalogSearch.Rank(
                normalized,
                merged,
                sourceSettings,
                // One list, not one page per source: twice the per-source limit.
                boundedLimit * 2),
            outcomes
                .Where(outcome => !outcome.Available)
                .Select(outcome => outcome.Provider)
                .ToArray());
    }

    private async Task<ProviderOutcome> SearchOneAsync(
        IReadingCatalogProvider provider,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!health.CanAttempt(provider.Key))
        {
            return new ProviderOutcome(provider.Key, [], Available: false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(ProviderTimeout);

        try
        {
            var results = await provider.SearchAsync(
                query,
                limit,
                timeout.Token);
            health.RecordSuccess(provider.Key);
            return new ProviderOutcome(provider.Key, results, Available: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Any other failure, including a timeout or an unexpected page layout, only
            // takes this source out of the answer.
            var unavailable = exception as ReadingSourceUnavailableException;
            logger.LogWarning(
                exception,
                "Reading source {Provider} could not be searched",
                provider.Key);
            health.RecordFailure(
                provider.Key,
                unavailable?.Kind ?? ReadingSourceFailureKind.Unavailable,
                unavailable?.RetryAfter);
            return new ProviderOutcome(provider.Key, [], Available: false);
        }
    }

    private sealed record ProviderOutcome(
        string Provider,
        IReadOnlyList<ReadingCatalogCandidate> Candidates,
        bool Available);
}
